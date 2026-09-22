using UnityEngine;

namespace Parkour
{
    internal static class VectorExtensions
    {
        // The horizontal part of a vector
        public static Vector3 Flat(this Vector3 vector)
        {
            return new Vector3(vector.x, 0f, vector.z);
        }
    }
}
