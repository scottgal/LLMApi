using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace LLMockApiClient.Controls;

public class JsonViewer : TextEditor
{
    public JsonViewer()
    {
        // Set default properties
        IsReadOnly = true;
        ShowLineNumbers = false;
        FontFamily = new FontFamily("Consolas");
        FontSize = 11;
        WordWrap = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;

        // Load JSON syntax highlighting
        LoadJsonHighlighting();

        // Apply dark theme
        ApplyDarkTheme();
    }

    private void LoadJsonHighlighting()
    {
        var xshdString = @"<?xml version=""1.0""?>
<SyntaxDefinition name=""JSON"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Boolean"" foreground=""#569CD6"" />
    <Color name=""Null"" foreground=""#569CD6"" />
    <Color name=""PropertyName"" foreground=""#9CDCFE"" />
    <Color name=""Punctuation"" foreground=""#D4D4D4"" />

    <RuleSet>
        <!-- Property names -->
        <Rule color=""PropertyName"">
            ""[^""\\]*(?:\\.[^""\\]*)*""\s*:
        </Rule>

        <!-- Strings -->
        <Rule color=""String"">
            ""[^""\\]*(?:\\.[^""\\]*)*""
        </Rule>

        <!-- Numbers -->
        <Rule color=""Number"">
            -?\d+\.?\d*([eE][+-]?\d+)?
        </Rule>

        <!-- Booleans -->
        <Keywords color=""Boolean"">
            <Word>true</Word>
            <Word>false</Word>
        </Keywords>

        <!-- Null -->
        <Keywords color=""Null"">
            <Word>null</Word>
        </Keywords>

        <!-- Punctuation -->
        <Rule color=""Punctuation"">
            [\{\}\[\],:]{
        </Rule>
    </RuleSet>
</SyntaxDefinition>";

        using (var reader = new XmlTextReader(new StringReader(xshdString)))
        {
            SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
    }

    private void ApplyDarkTheme()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
    }

    public void SetJson(string json)
    {
        try
        {
            // Parse and pretty print JSON
            var jsonDoc = JsonDocument.Parse(json);
            var prettyJson = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            Text = prettyJson;
        }
        catch
        {
            // If parsing fails, just show the raw text
            Text = json;
        }
    }
}