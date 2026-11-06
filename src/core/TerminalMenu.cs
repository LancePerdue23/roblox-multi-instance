using System.Text.Json;

namespace RobloxLaunchHelper;

// A line-based interface also works with screen readers and terminals without ANSI support.
internal static class TerminalMenu
{
    internal sealed record Action(string Label, string[] Command);
    internal static readonly Action[] Actions =
    [
        new("Launch Roblox", ["launch"]),
        new("Enable browser Play integration", ["integration", "on"]),
        new("Disable browser Play integration", ["integration", "off"]),
        new("Helper status", ["status"])
    ];

    internal const string Banner = """
         ____  __  __ ___
        |  _ \|  \/  |_ _|
        | |_) | |\/| || |
        |  _ <| |  | || |
        |_| \_\_|  |_|___|
          Roblox Launch Helper
        """;

    internal static async Task<int> RunAsync()
    {
        if(Console.IsInputRedirected||Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("The interactive menu requires a terminal. Use rmi --help for scriptable commands.");
            return 2;
        }
        Console.WriteLine(Banner);
        Console.WriteLine("Launch handles the singleton event automatically.\nWith browser integration enabled, just use Play on Roblox.");
        while(true)
        {
            Console.WriteLine();
            for(int i=0;i<Actions.Length;i++) Console.WriteLine($"  {i+1}. {Actions[i].Label}");
            Console.Write("  q. Exit\n> ");
            string? choice=Console.ReadLine()?.Trim();
            if(choice is null||choice.Equals("q",StringComparison.OrdinalIgnoreCase)) return 0;
            if(!int.TryParse(choice,out int index)||index<1||index>Actions.Length)
            {
                Console.WriteLine("Enter 1-4 or q.");
                continue;
            }
            Console.WriteLine();
            await Terminal.RunAsync(Actions[index-1].Command);
        }
    }

    internal static string Format(object data)
    {
        using var document=JsonDocument.Parse(JsonSerializer.Serialize(data,new JsonSerializerOptions
        { Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()} }));
        var output=new System.Text.StringBuilder();
        Render(document.RootElement,output,0);
        return output.ToString().TrimEnd();
    }

    static void Render(JsonElement value,System.Text.StringBuilder output,int indent)
    {
        string pad=new(' ',indent);
        if(value.ValueKind==JsonValueKind.Object)
        {
            foreach(var property in value.EnumerateObject())
            {
                string label=System.Text.RegularExpressions.Regex.Replace(property.Name,"(?<=[a-z])([A-Z])"," $1");
                output.Append(pad).Append(char.ToUpperInvariant(label[0])).Append(label.AsSpan(1)).Append(':');
                if(property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {output.AppendLine();Render(property.Value,output,indent+2);}
                else output.Append(' ').AppendLine(property.Value.ValueKind==JsonValueKind.Null?"Unavailable":property.Value.ToString());
            }
        }
        else if(value.ValueKind==JsonValueKind.Array)
        {
            if(value.GetArrayLength()==0) output.Append(pad).AppendLine("None");
            foreach(var item in value.EnumerateArray())
            {output.Append(pad).AppendLine("-");Render(item,output,indent+2);}
        }
        else output.Append(pad).AppendLine(value.ToString());
    }
}
