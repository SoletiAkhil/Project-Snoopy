using System.Text;
using Terminal = System.Console;

namespace Snoopy.Voice.Console;

internal static class ConsoleInput
{
    public static async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Terminal.IsInputRedirected)
        {
            return await Terminal.In.ReadLineAsync(cancellationToken);
        }

        // Console.ReadLine cannot be canceled on Windows. Poll keys without leaving an orphaned reader.
        var text = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Terminal.KeyAvailable)
            {
                await Task.Delay(40, cancellationToken);
                continue;
            }

            var key = Terminal.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Terminal.WriteLine();
                return text.ToString();
            }
            if (key.Key == ConsoleKey.Backspace && text.Length > 0)
            {
                text.Length--;
                Terminal.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
                Terminal.Write(key.KeyChar);
            }
        }
    }
}
