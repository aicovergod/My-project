// Refactor: OSRS-style login with per-account saves; removed Overworld hop; atomic file IO; PBKDF2.
using System;
using System.Reflection;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Utility methods that locate the active player object within the currently loaded scene.
    /// </summary>
    public static class PlayerLocator
    {
        /// <summary>
        /// Attempts to locate the player object by tag or by common player components.
        /// </summary>
        /// <param name="player">Outputs the resolved player GameObject when found.</param>
        /// <returns>True when a player object could be resolved.</returns>
        public static bool TryFindPlayer(out GameObject player)
        {
            player = TryFindByTag();
            if (player != null)
                return true;

            player = TryFindByController();
            if (player != null)
                return true;

            player = TryFindByComponent<PlayerMover>();
            if (player != null)
                return true;

            player = TryFindByComponent<PlayerHitpoints>();
            if (player != null)
                return true;

            player = null;
            return false;
        }

        private static GameObject TryFindByTag()
        {
            try
            {
                return GameObject.FindWithTag("Player");
            }
            catch (UnityException)
            {
                return null;
            }
        }

        private static GameObject TryFindByController()
        {
            var controller = ResolveComponentByType("Player.PlayerController, Assembly-CSharp")
                ?? ResolveComponentByType("PlayerController, Assembly-CSharp");
            return controller != null ? controller.gameObject : null;
        }

        private static GameObject TryFindByComponent<T>() where T : Component
        {
            var component = UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
            return component != null ? component.gameObject : null;
        }

        private static Component ResolveComponentByType(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type == null)
                return null;

            var instance = InvokeFindFirstObjectByType(type);
            if (instance is Component component && component != null)
                return component;
            if (instance is GameObject obj)
                return obj.GetComponent(type);

            var all = Resources.FindObjectsOfTypeAll(type);
            for (int i = 0; i < all.Length; i++)
            {
                switch (all[i])
                {
                    case Component comp when comp.gameObject.scene.IsValid():
                        return comp;
                    case GameObject go when go.scene.IsValid():
                        var resolved = go.GetComponent(type);
                        if (resolved != null)
                            return resolved;
                        break;
                }
            }

            return null;
        }

        private static object InvokeFindFirstObjectByType(Type type)
        {
            try
            {
                var methods = typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    var candidate = methods[i];
                    if (!candidate.IsGenericMethodDefinition || candidate.Name != "FindFirstObjectByType")
                        continue;

                    var parameters = candidate.GetParameters();
                    object[] args;
                    if (parameters.Length == 0)
                    {
                        args = Array.Empty<object>();
                    }
                    else if (parameters.Length == 1 && parameters[0].ParameterType.FullName == "UnityEngine.FindObjectsInactive")
                    {
                        var includeValue = Enum.ToObject(parameters[0].ParameterType, 1);
                        args = new[] { includeValue };
                    }
                    else
                    {
                        continue;
                    }

                    var generic = candidate.MakeGenericMethod(type);
                    return generic.Invoke(null, args);
                }
            }
            catch
            {
                // Reflection fallback failures can be ignored.
            }

            return null;
        }
    }
}
