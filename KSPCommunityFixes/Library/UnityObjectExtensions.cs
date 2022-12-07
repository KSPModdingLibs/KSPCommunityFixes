using System;
using System.Runtime.CompilerServices;

namespace KSPCommunityFixes
{
#pragma warning disable IDE0041 // Use 'is null' check
    static class UnityObjectExtensions
    {
        /// <summary>
        /// Perform a true reference equality comparison between two UnityEngine.Object references,
        /// ignoring the "a destroyed object is equal to null" Unity concept.<br/>
        /// Avoid the performance hit of using the <c>==</c> and <c>Equals()</c> overloads.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RefEquals(this UnityEngine.Object unityObject, UnityEngine.Object otherUnityObject)
        {
            return ReferenceEquals(unityObject, otherUnityObject);
        }

        /// <summary>
        /// Perform a true reference inequality comparison between two UnityEngine.Object references,
        /// ignoring the "a destroyed object is equal to null" Unity concept.<br/>
        /// Avoid the performance hit of using the <c>==</c> and <c>Equals()</c> overloads.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RefNotEquals(this UnityEngine.Object unityObject, UnityEngine.Object otherUnityObject)
        {
            return !ReferenceEquals(unityObject, otherUnityObject);
        }

        /// <summary>
        /// Equivalent as the Unity <c>==</c> and <c>Equals()</c> overloads, but 6-8 times faster.<br/>
        /// Use this if you want to perform an equality check where :<br/>
        /// - a destroyed <c>UnityEngine.Object</c> instance is considered equal to <c>null</c><br/>
        /// - two different destroyed <c>UnityEngine.Object</c> instances are not considered equal
        /// </summary>
        public static bool NotDestroyedRefEquals(this UnityEngine.Object unityObject, UnityEngine.Object otherUnityObject)
        {
            if (ReferenceEquals(unityObject, otherUnityObject))
                return true;

            if (ReferenceEquals(otherUnityObject, null) && unityObject.m_CachedPtr == IntPtr.Zero)
                return true;

            if (ReferenceEquals(unityObject, null) && otherUnityObject.m_CachedPtr == IntPtr.Zero)
                return true;

            return false;
        }

        /// <summary>
        /// Equivalent as the Unity <c>!=</c> and <c>!Equals()</c> overloads, but 6-8 times faster.<br/>
        /// Use this if you want to perform an equality check where :<br/>
        /// - a destroyed <c>UnityEngine.Object</c> instance is considered equal to <c>null</c><br/>
        /// - two different destroyed <c>UnityEngine.Object</c> instances are not considered equal
        /// </summary>
        public static bool NotDestroyedRefNotEquals(this UnityEngine.Object unityObject, UnityEngine.Object otherUnityObject)
        {
            if (ReferenceEquals(unityObject, otherUnityObject))
                return false;

            if (ReferenceEquals(otherUnityObject, null) && unityObject.m_CachedPtr == IntPtr.Zero)
                return false;

            if (ReferenceEquals(unityObject, null) && otherUnityObject.m_CachedPtr == IntPtr.Zero)
                return false;

            return true;
        }

        /// <summary>
        /// True if this <paramref name="unityObject"/> instance is destroyed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDestroyed(this UnityEngine.Object unityObject)
        {
            return unityObject.m_CachedPtr == IntPtr.Zero;
        }

        /// <summary>
        /// True if this <paramref name="unityObject"/> reference is <c>null</c>,
        /// ignoring the "a destroyed object is equal to null" Unity concept.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullRef(this UnityEngine.Object unityObject)
        {
            return ReferenceEquals(unityObject, null);
        }

        /// <summary>
        /// True if this <paramref name="unityObject"/> reference is not <c>null</c>,
        /// ignoring the "a destroyed object is equal to null" Unity concept.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNotNullRef(this UnityEngine.Object unityObject)
        {
            return !ReferenceEquals(unityObject, null);
        }

        /// <summary>
        /// True if this <paramref name="unityObject"/> reference is <c>null</c> or if the instance is destroyed<br/>
        /// Equivalent as testing <c><paramref name="unityObject"/> == null</c> but 4-5 times faster.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrDestroyed(this UnityEngine.Object unityObject)
        {
            return ReferenceEquals(unityObject, null) || unityObject.m_CachedPtr == IntPtr.Zero;
        }

        /// <summary>
        /// True if this <paramref name="unityObject"/> reference is not <c>null</c> and the instance is not destroyed<br/>
        /// Equivalent as testing <c><paramref name="unityObject"/> != null</c> but 4-5 times faster.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNotNullOrDestroyed(this UnityEngine.Object unityObject)
        {
            return !ReferenceEquals(unityObject, null) && unityObject.m_CachedPtr != IntPtr.Zero;
        }

        /// <summary>
        /// Return <c>null</c> when this <paramref name="unityObject"/> reference is <c>null</c> or destroyed, otherwise return the <paramref name="unityObject"/> instance<br/>
        /// Allow using null conditional and null coalescing operators with <c>UnityEngine.Object</c> derivatives while conforming to the "a destroyed object is equal to null" Unity concept.<br/>
        /// Example :<br/>
        /// <c>float x = myUnityObject.DestroyedAsNull()?.myFloatField ?? 0f;</c><br/>
        /// will evaluate to <c>0f</c> when <c>myUnityObject</c> is destroyed, instead of returning the value still available on the managed instance.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T DestroyedAsNull<T>(this T unityObject) where T : UnityEngine.Object
        {
            if (ReferenceEquals(unityObject, null) || unityObject.m_CachedPtr == IntPtr.Zero)
                return null;

            return unityObject;
        }
    }
#pragma warning restore IDE0041 // Use 'is null' check
}
