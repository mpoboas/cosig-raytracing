using System;

namespace Resources
{
    /// <summary>
    /// Named color enum living under the Resources namespace.
    /// Note: this is intentionally a small set of common colors; extend as needed.
    /// </summary>
    public enum Color
    {
        Clear,
        White,
        Black,
        Gray,
        Red,
        Green,
        Blue,
        Yellow,
        Cyan,
        Magenta,
        Orange,
        Brown,
        Purple,
        Pink
    }

    /// <summary>
    /// Utility extensions for the <see cref="Color"/> enum.
    /// </summary>
    public static class ColorExtensions
    {
        /// <summary>
        /// Convert the enum value to a UnityEngine.Color.
        /// Uses commonly-accepted RGB approximations for named colors.
        /// </summary>
        public static UnityEngine.Color ToUnityColor(this Color c)
        {
            switch (c)
            {
                case Color.Clear: return new UnityEngine.Color(0f, 0f, 0f, 0f);
                case Color.White: return UnityEngine.Color.white;
                case Color.Black: return UnityEngine.Color.black;
                case Color.Gray: return UnityEngine.Color.gray;
                case Color.Red: return UnityEngine.Color.red;
                case Color.Green: return UnityEngine.Color.green;
                case Color.Blue: return UnityEngine.Color.blue;
                case Color.Yellow: return UnityEngine.Color.yellow;
                case Color.Cyan: return UnityEngine.Color.cyan;
                case Color.Magenta: return UnityEngine.Color.magenta;
                case Color.Orange: return new UnityEngine.Color(1f, 0.6470588f, 0f);
                case Color.Brown: return new UnityEngine.Color(0.6470588f, 0.1647059f, 0.1647059f);
                case Color.Purple: return new UnityEngine.Color(0.5f, 0f, 0.5f);
                case Color.Pink: return new UnityEngine.Color(1f, 0.7529412f, 0.7960784f);
                default: return UnityEngine.Color.white;
            }
        }
    }
}

