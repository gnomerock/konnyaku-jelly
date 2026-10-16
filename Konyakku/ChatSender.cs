using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Konyakku;

/// <summary>Sends a line through the game's chat box, exactly as if typed. Must run on the framework thread.</summary>
internal static unsafe class ChatSender
{
    private const int MaxBytes = 500;

    public static void Send(string line)
    {
        line = line.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (line.Length == 0) return;

        while (Encoding.UTF8.GetByteCount(line) > MaxBytes)
            line = line[..^1];

        var uiModule = UIModule.Instance();
        if (uiModule == null) throw new InvalidOperationException("UI module is not available.");

        var str = Utf8String.FromString(line);
        try
        {
            uiModule->ProcessChatBoxEntry(str);
        }
        finally
        {
            str->Dtor(true);
        }
    }
}
