using System;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using Mono.Cecil;
using System.Linq;
using GDS.ASCII;
using System.Text;

namespace AssemblyHierarchy
{
    class MainClass
    {
        static Dictionary<string, HashSet<string>> _assemblies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        static IEnumerable<string> Roots
        {
            get
            {
                foreach(var root in _assemblies.Keys)
                {
                    var isRoot = true;
                    foreach(var value in _assemblies.Values)
                    {
                        if (value.Contains(root))
                        {
                            isRoot = false;
                            break;
                        }
                    }

                    if (isRoot)
                        yield return root;
                }
            }
        }

        static bool IsAssemblyValid(AssemblyDefinition assembly)
        {
            try
            {
                var isIQVIA = false;

                if (assembly.HasCustomAttributes)
                {
                    var company = (from c in assembly.CustomAttributes
                                  where c.AttributeType.Name == typeof(AssemblyCompanyAttribute).Name
                                  select c).FirstOrDefault();

                    if (company?.HasConstructorArguments == true)
                    {
                        var value = company.ConstructorArguments.First().Value?.ToString() ?? string.Empty;
                        if (value.IndexOf("IQVIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            value.IndexOf("IMS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            value.IndexOf("Cegedim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            value.IndexOf("Quintile", StringComparison.OrdinalIgnoreCase) >= 0)
                            isIQVIA = true;
                    }
                }
                return isIQVIA;
            }
            catch
            {
                return false;
            }
        }

        static void AddEntry(AssemblyDefinition parent, AssemblyDefinition child)
        {
            if (! IsAssemblyValid(child))
                return;

            if (! _assemblies.TryGetValue(parent.FullName, out HashSet<string> children))
            {
                children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _assemblies[parent.FullName] = children;
            }
            children.Add(child.FullName);
        }

        static AssemblyDefinition GetAssembly(string filename)
        {
            try
            {
			    var assembly = AssemblyDefinition.ReadAssembly(filename);

                var isIQVIA = false;

                if (assembly.HasCustomAttributes)
                {
                    var company = (from c in assembly.CustomAttributes
                                  where c.AttributeType.Name == typeof(AssemblyCompanyAttribute).Name
                                  select c).FirstOrDefault();

                    if (company?.HasConstructorArguments == true)
                    {
                        var value = company.ConstructorArguments.First().Value?.ToString() ?? string.Empty;
                        if (value.IndexOf("IQVIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            value.IndexOf("IMS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            value.IndexOf("Cegedim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            value.IndexOf("Quintile", StringComparison.OrdinalIgnoreCase) >= 0)
                            isIQVIA = true;
                    }
                }
                if (isIQVIA)
                    return assembly;
                else
                    return null;
            }
            catch(BadImageFormatException)
            {
                // Ignore this
                return null;
            }
            catch(Exception e)
            {
                var file = new FileInfo(filename);
                Console.Error.WriteLine($"{ file.Name }: { e.Message }");
                return null;
            }
        }

        static void AnalyzeAssembly(AssemblyDefinition assembly)
        {
            var assemblies = new HashSet<AssemblyDefinition>();

            Action< ICollection<TypeDefinition> > addTypes = null;
            Action< ICollection<CustomAttribute> > addAttributes;
            Action< ICollection<EventDefinition> > addEvents;
            Action< MethodDefinition > addMethod;
            Action< ICollection<MethodDefinition> > addMethods;
            Action< ICollection<ParameterDefinition> > addParameters;

            Action<TypeReference> addType = (type) =>
            {
                try
                {
                    var t = type?.Resolve();
                    if (t != null)
                    {
                        var a = t.Module.Assembly;
                        if (a != null && a !=  assembly)
                            AddEntry(assembly, a);
                    }
                }
                catch(Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(e.Message);
                }
            };

            addAttributes = (attributes) =>
            {
                if (attributes == null)
                    return;

                foreach(var attr in attributes)
                {
                    addType(attr.AttributeType);
                }
            };

            addEvents = (events) =>
            {
                if (events == null)
                    return;

                foreach(var evt in events)
                {
                    addType(evt.EventType);
                }
            };

            addParameters = (parameters) =>
            {
                if (parameters == null)
                    return;
                foreach(var parameter in parameters)
                {
                    addAttributes(parameter.CustomAttributes);
                    addType(parameter.ParameterType);
                }
            };

            addMethod = (method) =>
            {
                if (method == null)
                    return;
                addAttributes(method.CustomAttributes);
                addAttributes(method.MethodReturnType?.CustomAttributes);
                addType(method.MethodReturnType?.ReturnType);
                addType(method.ReturnType);
                addParameters(method.Parameters);
                var body = method.Body;
                if (body != null)
                {
                    foreach(var v in body.Variables)
                        addType(v.VariableType);
                    foreach(var i in body.Instructions)
                    {
                        try
                        {
                            var o = i.Operand;
                            if (o is FieldDefinition f)
                            {
                                var f2 = f.Resolve();
                                addType(f2?.FieldType);
                                addType(f2?.DeclaringType);
                            }
                            else if (o is MethodReference mr)
                            {
                                var mr2 = mr.Resolve();
                                addType(mr2?.ReturnType);
                                addType(mr2?.DeclaringType);
                            }
                            else if (o is MethodDefinition md)
                            {
                                var md2 = md.Resolve();
                                addType(md2?.ReturnType);
                                addType(md2?.DeclaringType);
                            }
                        }
                        catch(AssemblyResolutionException e)
                        {
                        }
                    }
                }
            };

            addMethods = (methods) =>
            {
                if (methods == null)
                    return;

                foreach(var method in methods)
                {
                    addMethod(method);
                }
            };

            addTypes = (types) =>
            {
                if (types == null)
                    return;

                foreach(var type in types)
                {
                    addAttributes(type.CustomAttributes);
                    addEvents(type.Events);
                    addTypes(type.NestedTypes);

                    foreach(var field in type.Fields)
                    {
                        addAttributes(field.CustomAttributes);
                        addType(field.FieldType);
                    }

                    foreach(var prop in type.Properties)
                    {
                        addAttributes(prop.CustomAttributes);
                        addType(prop.PropertyType);
                        addMethod(prop.GetMethod);
                        addMethod(prop.SetMethod);
                        addMethods(prop.OtherMethods);
                    }

                    addMethods(type.Methods);
                }
            };

            // All used attribures
            addAttributes(assembly.CustomAttributes);

            // All Mdules
            foreach(var module in assembly.Modules)
            {
                addAttributes(module.CustomAttributes);
                addTypes(module.Types);
            }
        }

        static void AddAssembly(string filename)
        {
            var a = GetAssembly(filename);
            if (a == null)
                return;

            AnalyzeAssembly(a);

            //foreach(var module in a.Modules)
            //{
            //    foreach(var reference in module.AssemblyReferences)
            //    {
            //        var f = Path.Combine(reference.Name+".dll");
            //        if (File.Exists(f))
            //        {
            //            var b = GetAssembly(f);
            //            if (b != null)
            //                AddEntry2(a.FullName, b.FullName);
            //        }
            //    }
            //}
        }

        static Node BuildTree(string parent, int deep = 0, HashSet<string> visited = null)
        {
            if (visited == null)
                visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var node = new Node(parent.Split(',')[0]);
            if (_assemblies.ContainsKey(parent))
            {
                var toAdd = new List<string>();

                foreach(var child in _assemblies[parent])
                {
                    if (! visited.Contains(child))
                    {
                        visited.Add(child);
                        toAdd.Add(child);
                    }
                }

                foreach(var child in toAdd)
                {
                    var c = BuildTree(child, deep+1, visited);
                    if (c != null)
                        node.Childs.Add(c);
                }
            }

            return node;
        }

        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var filesToLoad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reversed = false;
            var currentDirectory = Environment.CurrentDirectory;

            foreach(var arg in args)
            {
                if (arg.Equals("--reverse", StringComparison.OrdinalIgnoreCase))
                    reversed = true;
                else if (arg.Equals("-r", StringComparison.OrdinalIgnoreCase))
                    reversed = true;
                else
                {
                    var path = Path.Combine(currentDirectory, arg);
                    if (Directory.Exists(path))
                    {
                        path = new DirectoryInfo(path).FullName;
                        foreach(var file in Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories))
                        {
                            filesToLoad.Add(file);
                        }
                        foreach(var file in Directory.GetFiles(path, "*.exe", SearchOption.AllDirectories))
                        {
                            filesToLoad.Add(file);
                        }
                    }
                    else if (File.Exists(path))
                    {
                        filesToLoad.Add(path);
                    }
                }
            }

            foreach(var file in filesToLoad)
            {
                try
                { 
                    var f = new FileInfo(file);
                    Environment.CurrentDirectory = f.Directory.FullName;
                    AddAssembly(file);
                }
                finally
                {
                    Environment.CurrentDirectory = currentDirectory;
                }
            }

            if (reversed)
            {
                var d = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                foreach(var a in _assemblies.Keys)
                {
                    foreach(var b in _assemblies[a])
                    {
                        if (! d.TryGetValue(b, out HashSet<string> x))
                        {
                            d[b] = x = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        }
                        x.Add(a);
                    }
                }

                _assemblies = d; 
            }

            foreach(var a in Roots)
            {
                var tree = BuildTree(a);
                Console.WriteLine(ASCIITree.GetTree(tree, "DisplayText", "Childs"));
            }

            Console.OutputEncoding = Encoding.Default;
        }
    }
}
