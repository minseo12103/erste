using System;
using UnityEngine;

namespace ShapeCoop
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class MapGoalArea : MonoBehaviour
    {
        public event Action<Collider2D> Entered;
        void OnTriggerEnter2D(Collider2D other) => Entered?.Invoke(other);
    }
}
