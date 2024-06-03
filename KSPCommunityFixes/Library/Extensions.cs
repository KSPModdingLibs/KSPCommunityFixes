using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes
{
    static class Extensions
    {
        /// <summary>
        /// Get an assembly qualified type name in the "assemblyName:typeName" format
        /// </summary>
        public static string AssemblyQualifiedName(this object obj)
        {
            Type type = obj.GetType();
            return $"{type.Assembly.GetName().Name}:{type.Name}";
        }

        public static bool IsPAWOpen(this Part part)
        {
            return part.PartActionWindow.IsNotNullOrDestroyed() && part.PartActionWindow.isActiveAndEnabled;
        }
    }
}
