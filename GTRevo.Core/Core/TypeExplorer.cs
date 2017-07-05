﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GTRevo.Core.Core
{
    public class TypeExplorer : ITypeExplorer
    {
        public virtual IEnumerable<Assembly> GetAllReferencedAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
               /*.Where(a => a.GetName().Name.StartsWith("System") == false)*/;
        }

        public IEnumerable<Type> GetAllTypes()
        {
            var assemblies = GetAllReferencedAssemblies();
            return assemblies.SelectMany(x => x.GetTypes());
        }
    }
}
