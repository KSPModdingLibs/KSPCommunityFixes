using System;

namespace KSPCommunityFixes
{
    static class UnityExtensions
    {
        /// <summary>
        /// Return "null" when the UnityEngine.Object instance is either null or destroyed/not initialized.<br/>
        /// Allow using null conditional and null coalescing operators with classes deriving from UnityEngine.Object
        /// while keeping the "a destroyed object is equal to null" Unity concept.<br/>
        /// Example :<br/>
        /// <c>float x = myUnityObject.AsNull()?.myFloatField ?? 0f;</c><br/>
        /// will evaluate to <c>0f</c> when <c>myUnityObject</c> is destroyed, instead of returning the value still
        /// available on the destroyed instance.
        /// </summary>
        public static T AsNull<T>(this T unityObject) where T : UnityEngine.Object
        {
            if (ReferenceEquals(unityObject, null) || unityObject.m_CachedPtr == IntPtr.Zero)
                return null;

            return unityObject;
        }
    }
}
