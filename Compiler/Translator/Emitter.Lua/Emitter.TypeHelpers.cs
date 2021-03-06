using Bridge.Contract;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bridge.Translator.Lua
{
    public partial class Emitter
    {
        public virtual int CompareTypeInfosByName(ITypeInfo x, ITypeInfo y)
        {
            if (x == y)
            {
                return 0;
            }

            if (x.Key == Emitter.ROOT)
            {
                return -1;
            }

            if (y.Key == Emitter.ROOT)
            {
                return 1;
            }

            if (!this.TypeDefinitions.ContainsKey(x.Key))
            {
                throw new Exception("Class with name '" + x.Key + "' is not found in the assembly, probably rebuild is required");
            }

            if (!this.TypeDefinitions.ContainsKey(y.Key))
            {
                throw new Exception("Class with name '" + y.Key + "' is not found in the assembly, probably rebuild is required");
            }

            var xTypeDefinition = this.TypeDefinitions[x.Key];
            var yTypeDefinition = this.TypeDefinitions[y.Key];

            return xTypeDefinition.FullName.CompareTo(yTypeDefinition.FullName);
        }

        public virtual int CompareTypeInfosByPriority(ITypeInfo x, ITypeInfo y)
        {
            if (x == y)
            {
                return 0;
            }

            if (x.Key == Emitter.ROOT)
            {
                return -1;
            }

            if (y.Key == Emitter.ROOT)
            {
                return 1;
            }

            var xTypeDefinition = this.TypeDefinitions[x.Key];
            var yTypeDefinition = this.TypeDefinitions[y.Key];

            var xPriority = this.GetPriority(xTypeDefinition);
            var yPriority = this.GetPriority(yTypeDefinition);

            return -xPriority.CompareTo(yPriority);
        }

        public virtual bool IsInheritedFrom(ITypeInfo x, ITypeInfo y)
        {
            if (x == y)
            {
                return false;
            }

            var inherits = false;
            var xTypeDefinition = this.TypeDefinitions[x.Key];
            var yTypeDefinition = this.TypeDefinitions[y.Key];

            if (Helpers.IsSubclassOf(xTypeDefinition, yTypeDefinition, this) ||
                (yTypeDefinition.IsInterface && Helpers.IsImplementationOf(xTypeDefinition, yTypeDefinition, this)) ||
                Helpers.IsTypeArgInSubclass(xTypeDefinition, yTypeDefinition, this))
            {
                inherits = true;
            }

            return inherits;
        }

        private void QuickSort(IList<ITypeInfo> list, int l, int r) {
            ITypeInfo temp;
            ITypeInfo x = list[l + (r - l) / 2];
            int i = l;
            int j = r;
            while(i <= j) {

                while(this.CompareTypeInfosByPriority(list[i], x) == -1) {
                    i++;
                }

                while(this.CompareTypeInfosByPriority(list[j], x) == 1) {
                    j--;
                }

                if(i <= j) {
                    if(this.CompareTypeInfosByPriority(list[i], list[j]) != 0) {
                        temp = list[i];
                        list[i] = list[j];
                        list[j] = temp;
                    }

                    i++;
                    j--;
                }
            }

            if(i < r) {
                this.QuickSort(list, i, r);
            }


            if(l < j) {
                this.QuickSort(list, l, j);
            }
        }

        public virtual void SortTypesByInheritance() {
            if(this.Types.Count > 0) {
                List<List<ITypeInfo>> typesList = new List<List<ITypeInfo>>();
                typesList.Add(this.Types.ToList());

                while(true) {
                    HashSet<ITypeInfo> parentTypes = new HashSet<ITypeInfo>();
                    var lastTypes = typesList.Last();
                    foreach(var type in lastTypes) {
                        var parents = this.GetParents(type.Type);
                        foreach(var parent in parents) {
                            if(parent != type) {
                                parentTypes.Add(parent);
                            }
                        }
                    }

                    if(parentTypes.Count == 0) {
                        break;
                    }

                    typesList.Add(parentTypes.ToList());
                }

                HashSet<ITypeInfo> typesSet = new HashSet<ITypeInfo>();

                typesList.Reverse();
                foreach(var types in typesList) {
                    foreach(var type in types) {
                        typesSet.Add(type);
                    }
                }

                this.Types.Clear();
                this.Types.AddRange(typesSet);
            }
        }

        /*
        public virtual void SortTypesByInheritance()
        {
            this.Log.Trace("Sorting types by inheritance...");

            if (this.Types.Count > 0)
            {
                this.TopologicalSort();

                //this.Types.Sort has strange effects for items with 0 priority

                this.Log.Trace("Priority sorting...");

                this.QuickSort(this.Types, 0, this.Types.Count - 1);

                this.Log.Trace("Priority sorting done");
            }
            else
            {
                this.Log.Trace("No types to sort");
            }

            this.Log.Trace("Sorting types by inheritance done");
        }*/

        private Stack<IType> activeTypes;
        public IList<ITypeInfo> GetParents(IType type, IList<ITypeInfo> list = null, bool includeSelf = false)
        {
            if (list == null)
            {
                activeTypes = new Stack<IType>();
                list = new List<ITypeInfo>();
            }

            var typeDef = type.GetDefinition() ?? type;

            if (activeTypes.Contains(typeDef))
            {
                return list;
            }

            activeTypes.Push(typeDef);

            var types = type.GetAllBaseTypes();
            foreach (var t in types)
            {
                var bType = BridgeTypes.Get(t, true);

                if (bType != null && bType.TypeInfo != null && (includeSelf || bType.Type != typeDef))
                {
                    list.Add(bType.TypeInfo);
                }

                if (t.TypeArguments.Count > 0)
                {
                    foreach (var typeArgument in t.TypeArguments)
                    {
                        this.GetParents(typeArgument, list, true);
                    }
                }
            }

            activeTypes.Pop();
            return includeSelf ? list : list.Distinct().ToList();
        }

        public virtual TypeDefinition GetTypeDefinition()
        {
            return this.TypeDefinitions[this.TypeInfo.Key];
        }

        public virtual TypeDefinition GetTypeDefinition(IType type)
        {
            return this.BridgeTypes.Get(type).TypeDefinition;
        }

        public virtual TypeDefinition GetTypeDefinition(AstType reference, bool safe = false)
        {
            var resolveResult = this.Resolver.ResolveNode(reference, this) as TypeResolveResult;
            var type = this.BridgeTypes.Get(resolveResult.Type, safe);
            return type != null ? type.TypeDefinition : null;
        }

        public virtual TypeDefinition GetBaseTypeDefinition()
        {
            return this.GetBaseTypeDefinition(this.GetTypeDefinition());
        }

        public virtual TypeDefinition GetBaseTypeDefinition(TypeDefinition type)
        {
            var reference = type.BaseType;

            if (reference == null)
            {
                return null;
            }

            return this.BridgeTypes.Get(reference).TypeDefinition;
        }

        public virtual TypeDefinition GetBaseMethodOwnerTypeDefinition(string methodName, int genericParamCount)
        {
            TypeDefinition type = this.GetBaseTypeDefinition();

            while (true)
            {
                var methods = type.Methods.Where(m => m.Name == methodName);

                foreach (var method in methods)
                {
                    if (genericParamCount < 1 || method.GenericParameters.Count == genericParamCount)
                    {
                        return type;
                    }
                }

                type = this.GetBaseTypeDefinition(type);
            }
        }

        public virtual string GetTypeHierarchy() {
            StringBuilder sb = new StringBuilder();
            sb.Append("{ ");

            var list = new List<string>();
            foreach(var t in this.TypeInfo.GetBaseTypes(this)) {
                var name = BridgeTypes.ToJsName(t, this, BridgeTypes.ToNameTypeEnum.Extend);
                list.Add(name);
            }

            if(list.Count > 0 && list[0] == "Object") {
                list.RemoveAt(0);
            }
            if(list.Count == 0) {
                return "";
            }

            bool needComma = false;
            foreach(var item in list) {
                if(needComma) {
                    sb.Append(", ");
                }

                needComma = true;
                sb.Append(item);
            }

            sb.Append(" }");
            return sb.ToString();
        }

        public virtual int GetPriority(TypeDefinition type)
        {
            var attr = type.CustomAttributes.FirstOrDefault(a =>
            {
                return a.AttributeType.FullName == "Bridge.PriorityAttribute";
            });

            if (attr != null)
            {
                return System.Convert.ToInt32(attr.ConstructorArguments[0].Value);
            }

            var baseType = this.GetBaseTypeDefinition(type);

            if (baseType != null)
            {
                return this.GetPriority(baseType);
            }

            return 0;
        }
    }
}