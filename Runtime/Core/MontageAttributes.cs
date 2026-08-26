using System;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇动作块分类特性。
    /// 用于在编辑器右键菜单或添加窗口中进行层级分组（如 "Visual", "Audio", "Camera" 等）。
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
    /// 蒙太奇动作块颜色特性。
    /// 用于定义动作块在时间轴轨道上的色块十六进制值（例如 "#FF5500" 或 "#0d3b66"）。
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
    /// 蒙太奇动作块显示名称特性。
    /// 用于覆盖类名的默认显示，提供更友好的中文/英文名称。
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
