namespace GameHelper.Diagnostics;

using System;
using System.IO;
using System.Text;

internal sealed class DiagnosticConsoleWriter(TextWriter original, RuntimeDiagnosticSession session, string stream) : TextWriter
{
    private readonly StringBuilder line = new();
    private readonly object gate = new();
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value) => this.Write(value.ToString());

    public override void Write(string? value)
    {
        if (value == null) return;
        lock (this.gate)
        {
            foreach (var character in value)
            {
                if (character == '\n') this.EmitLine();
                else if (character != '\r' && this.line.Length < 8192) this.line.Append(character);
            }
            // Keep the existing Debug console behaviour. A missing console is harmless.
            try { original.Write(value); } catch (IOException) { }
        }
    }

    public override void WriteLine(string? value) => this.Write((value ?? string.Empty) + Environment.NewLine);
    public override void WriteLine() => this.Write(Environment.NewLine);
    public override void Flush()
    {
        lock (this.gate)
        {
            if (this.line.Length > 0) this.EmitLine();
            try { original.Flush(); } catch (IOException) { }
        }
    }

    private void EmitLine()
    {
        if (this.line.Length > 0) session.ConsoleLine(this.line.ToString(), stream);
        this.line.Clear();
    }
}
