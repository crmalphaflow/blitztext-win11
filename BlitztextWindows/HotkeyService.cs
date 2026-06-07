using System.Runtime.InteropServices;

namespace BlitztextWindows;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkSpace = 0x20;
    private const uint VkD = 0x44;
    private const uint VkE = 0x45;
    private const uint VkF13 = 0x7C;
    private const uint VkF14 = 0x7D;
    private const uint VkF15 = 0x7E;
    private const uint VkF16 = 0x7F;
    private const uint VkNumPad1 = 0x61;
    private const uint VkNumPad2 = 0x62;
    private const uint VkNumPad3 = 0x63;
    private const uint VkNumPad4 = 0x64;
    private IntPtr hwnd;
    private bool hookAdded;
    private int maxHotkeyId;

    public event EventHandler<WorkflowKind>? HotkeyPressed;
    public event EventHandler? GhostPressed;
    public event EventHandler<int>? TextShortcutPressed;

    public void Register(
        IntPtr windowHandle,
        HotkeyBindings? bindings = null,
        IEnumerable<TextShortcutSettings>? textShortcuts = null)
    {
        hwnd = windowHandle;
        if (!hookAdded)
        {
            var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
            source.AddHook(WndProc);
            hookAdded = true;
        }

        UnregisterAll();
        var nextId = 1;
        Register(nextId++, WorkflowKind.Transcribe, ModControl | ModShift, VkSpace);
        Register(nextId++, WorkflowKind.Improve, ModControl | ModAlt, VkSpace);
        Register(nextId++, WorkflowKind.Calm, ModControl | ModShift, VkD);

        Register(nextId++, WorkflowKind.Transcribe, 0, bindings?.Transcribe is { } transcribe ? (uint)transcribe : VkNumPad1);
        Register(nextId++, WorkflowKind.Improve, 0, bindings?.Improve is { } improve ? (uint)improve : VkNumPad2);
        Register(nextId++, WorkflowKind.Calm, 0, bindings?.Calm is { } calm ? (uint)calm : VkNumPad3);

        Register(nextId++, WorkflowKind.Transcribe, 0, VkF13);
        Register(nextId++, WorkflowKind.Improve, 0, VkF14);
        Register(nextId++, WorkflowKind.Calm, 0, VkF15);
        RegisterGhost(nextId++, 0, bindings?.Ghost is { } ghost ? (uint)ghost : VkNumPad4);
        RegisterGhost(nextId++, 0, VkF16);

        if (textShortcuts is not null)
        {
            foreach (var shortcut in textShortcuts.Where(shortcut => shortcut.Hotkey.HasValue))
            {
                RegisterTextShortcut(nextId++, shortcut.Id, 0, (uint)shortcut.Hotkey!.Value);
            }
        }

        maxHotkeyId = nextId - 1;
    }

    private void Register(int id, WorkflowKind kind, uint modifiers, uint key)
    {
        idToWorkflow[id] = kind;
        RegisterHotKey(hwnd, id, modifiers, key);
    }

    private void RegisterGhost(int id, uint modifiers, uint key)
    {
        ghostHotkeyIds.Add(id);
        RegisterHotKey(hwnd, id, modifiers, key);
    }

    private void RegisterTextShortcut(int id, int shortcutId, uint modifiers, uint key)
    {
        idToTextShortcut[id] = shortcutId;
        RegisterHotKey(hwnd, id, modifiers, key);
    }

    private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return IntPtr.Zero;
        }

        handled = true;
        if (idToWorkflow.TryGetValue(wParam.ToInt32(), out var kind))
        {
            HotkeyPressed?.Invoke(this, kind);
        }
        else if (ghostHotkeyIds.Contains(wParam.ToInt32()))
        {
            GhostPressed?.Invoke(this, EventArgs.Empty);
        }
        else if (idToTextShortcut.TryGetValue(wParam.ToInt32(), out var shortcutId))
        {
            TextShortcutPressed?.Invoke(this, shortcutId);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        UnregisterAll();
    }

    private readonly Dictionary<int, WorkflowKind> idToWorkflow = [];
    private readonly HashSet<int> ghostHotkeyIds = [];
    private readonly Dictionary<int, int> idToTextShortcut = [];

    private void UnregisterAll()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        for (var id = 1; id <= maxHotkeyId; id++)
        {
            UnregisterHotKey(hwnd, id);
        }

        idToWorkflow.Clear();
        ghostHotkeyIds.Clear();
        idToTextShortcut.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
