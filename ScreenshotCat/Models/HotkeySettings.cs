namespace ScreenshotCat.Models;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008
}

public readonly record struct HotkeyBinding(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private const uint VkTab = 0x09;
    private const uint VkReturn = 0x0D;
    private const uint VkEscape = 0x1B;
    private const uint VkSpace = 0x20;
    private const uint VkPageUp = 0x21;
    private const uint VkPageDown = 0x22;
    private const uint VkEnd = 0x23;
    private const uint VkHome = 0x24;
    private const uint VkLeft = 0x25;
    private const uint VkUp = 0x26;
    private const uint VkRight = 0x27;
    private const uint VkDown = 0x28;
    private const uint VkPrintScreen = 0x2C;
    private const uint VkInsert = 0x2D;
    private const uint VkDelete = 0x2E;
    private const uint VkPause = 0x13;
    private const uint VkScroll = 0x91;
    private const uint VkF1 = 0x70;

    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tab"] = VkTab,
        ["Enter"] = VkReturn,
        ["Return"] = VkReturn,
        ["Esc"] = VkEscape,
        ["Escape"] = VkEscape,
        ["Space"] = VkSpace,
        ["PageUp"] = VkPageUp,
        ["PgUp"] = VkPageUp,
        ["PageDown"] = VkPageDown,
        ["PgDn"] = VkPageDown,
        ["End"] = VkEnd,
        ["Home"] = VkHome,
        ["Left"] = VkLeft,
        ["Up"] = VkUp,
        ["Right"] = VkRight,
        ["Down"] = VkDown,
        ["PrintScreen"] = VkPrintScreen,
        ["PrtSc"] = VkPrintScreen,
        ["Insert"] = VkInsert,
        ["Delete"] = VkDelete,
        ["Del"] = VkDelete,
        ["Pause"] = VkPause,
        ["ScrollLock"] = VkScroll,
        ["Scroll Lock"] = VkScroll
    };

    public static HotkeyBinding ScrollLock => new(HotkeyModifiers.None, VkScroll);

    public static HotkeyBinding CtrlAltN => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'N');

    public bool IsReservedForAnnotation => VirtualKey == VkTab;

    public static bool TryParse(string? value, out HotkeyBinding binding, out string error)
    {
        binding = default;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "快捷键不能为空。";
            return false;
        }

        var parts = value
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "请输入一个有效快捷键。";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        uint? virtualKey = null;
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                if ((modifiers & HotkeyModifiers.Control) != 0)
                {
                    error = "Ctrl 重复出现。";
                    return false;
                }

                modifiers |= HotkeyModifiers.Control;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                if ((modifiers & HotkeyModifiers.Alt) != 0)
                {
                    error = "Alt 重复出现。";
                    return false;
                }

                modifiers |= HotkeyModifiers.Alt;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                if ((modifiers & HotkeyModifiers.Shift) != 0)
                {
                    error = "Shift 重复出现。";
                    return false;
                }

                modifiers |= HotkeyModifiers.Shift;
                continue;
            }

            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)
                || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                if ((modifiers & HotkeyModifiers.Win) != 0)
                {
                    error = "Win 重复出现。";
                    return false;
                }

                modifiers |= HotkeyModifiers.Win;
                continue;
            }

            if (virtualKey is not null)
            {
                error = "一个快捷键只能包含一个普通按键。";
                return false;
            }

            if (!TryParseVirtualKey(part, out var parsedKey))
            {
                error = $"不支持按键“{part}”。支持 A-Z、0-9、F1-F24 和常用功能键。";
                return false;
            }

            virtualKey = parsedKey;
        }

        if (virtualKey is null)
        {
            error = "快捷键必须包含一个普通按键，不能只有修饰键。";
            return false;
        }

        binding = new HotkeyBinding(modifiers, virtualKey.Value);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & HotkeyModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((Modifiers & HotkeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }
        if ((Modifiers & HotkeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }
        if ((Modifiers & HotkeyModifiers.Win) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyDisplayName(VirtualKey));
        return string.Join('+', parts);
    }

    private static bool TryParseVirtualKey(string part, out uint key)
    {
        if (part.Length == 1)
        {
            var character = char.ToUpperInvariant(part[0]);
            if ((character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9'))
            {
                key = character;
                return true;
            }
        }

        if (part.Length is 2 or 3
            && (part[0] == 'F' || part[0] == 'f')
            && int.TryParse(part.AsSpan(1), out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            key = VkF1 + (uint)(functionNumber - 1);
            return true;
        }

        var compact = part.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (NamedKeys.TryGetValue(part, out key) || NamedKeys.TryGetValue(compact, out key))
        {
            return true;
        }

        key = 0;
        return false;
    }

    private static string GetKeyDisplayName(uint key)
    {
        if ((key >= 'A' && key <= 'Z') || (key >= '0' && key <= '9'))
        {
            return ((char)key).ToString();
        }

        if (key >= VkF1 && key <= VkF1 + 23)
        {
            return $"F{key - VkF1 + 1}";
        }

        return key switch
        {
            VkTab => "Tab",
            VkReturn => "Enter",
            VkEscape => "Esc",
            VkSpace => "Space",
            VkPageUp => "PageUp",
            VkPageDown => "PageDown",
            VkEnd => "End",
            VkHome => "Home",
            VkLeft => "Left",
            VkUp => "Up",
            VkRight => "Right",
            VkDown => "Down",
            VkPrintScreen => "PrintScreen",
            VkInsert => "Insert",
            VkDelete => "Delete",
            VkPause => "Pause",
            VkScroll => "Scroll Lock",
            _ => $"VK_{key:X2}"
        };
    }
}

public sealed record HotkeySettings(HotkeyBinding PrimaryCapture, HotkeyBinding SecondaryCapture)
{
    public static HotkeySettings Default { get; } = new(HotkeyBinding.ScrollLock, HotkeyBinding.CtrlAltN);

    public bool TryValidate(out string error)
    {
        if (PrimaryCapture == SecondaryCapture)
        {
            error = "两个截图快捷键不能相同。";
            return false;
        }

        if (PrimaryCapture.IsReservedForAnnotation || SecondaryCapture.IsReservedForAnnotation)
        {
            error = "Tab 相关组合已用于批注操作，不能绑定为截图快捷键。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
