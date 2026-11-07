using System;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Categorizes an action block class in editor creation menus (e.g., "Visual", "Audio", "Gameplay").
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class MontageCategoryAttribute : Attribute
    {
        public string Category { get; }

        public MontageCategoryAttribute(string category)
        {
            Category = category ?? string.Empty;
        }
    }

    /// <summary>
    /// Defines the timeline track block color in hex format (e.g., "#FF5500" or "#0d3b66").
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class MontageColorAttribute : Attribute
    {
        public string HexColor { get; }

        public MontageColorAttribute(string hexColor)
        {
            HexColor = hexColor ?? "#3a3a3a";
        }
    }

    /// <summary>
    /// Overrides the default display name of an action block class in the editor UI.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class MontageDisplayNameAttribute : Attribute
    {
        public string DisplayName { get; }

        public MontageDisplayNameAttribute(string displayName)
        {
            DisplayName = displayName ?? string.Empty;
        }
    }
}
