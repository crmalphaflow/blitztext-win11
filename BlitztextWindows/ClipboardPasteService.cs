using System.Runtime.InteropServices;

namespace BlitztextWindows;

public sealed class ClipboardPasteService
{
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const uint KeyeventfKeyup = 0x0002;

    public void PasteClipboardText()
    {
        Thread.Sleep(120);
        SendKey(VkControl, false);
        SendKey(VkV, false);
        SendKey(VkV, true);
        SendKey(VkControl, true);
    }

    private static void SendKey(ushort key, bool keyUp)
    {
        var input = new Input
        {
            Type = 1,
            U = new InputUnion
            {
                KeyboardInput = new KeyboardInput
                {
                    VirtualKey = key,
                    Flags = keyUp ? KeyeventfKeyup : 0
                }
            }
        };

        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput KeyboardInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
