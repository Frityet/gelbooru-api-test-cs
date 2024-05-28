
using System.Text.Json;
using System.Text.Json.Serialization;

const string API_KEY = "";
const string USER_ID = "";
const int TAGLIST_COUNT = 10098;

var completedTagLists = new HashSet<int>();
//check every file in the pages directory
foreach (var file in Directory.EnumerateFiles("pages"))
{
    var page = int.Parse(Path.GetFileNameWithoutExtension(file));
    completedTagLists.Add(page);
}

if (args[0] != "--sync")
{
    int completedTasks = 0;
    var tasks = new Task<bool>[TAGLIST_COUNT - completedTagLists.Count];
    try {
        for (int i = 0, idx = 0; i < TAGLIST_COUNT; i++)
        {
            if (completedTagLists.Contains(i))
                continue;

            //2 vars (runningTasks and idx) so theres no race condition
            tasks[idx++] = WriteTagsToDiskAsync(i).ContinueWith((task) => {
                if (task.Result)
                    Interlocked.Increment(ref completedTasks);
                return task.Result;
            });
        }

        await Task.WhenAll(tasks);
    } catch (Exception ex) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("Exception reached: " + ex.Message);
    }

    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"Statistics:");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Completed tasks: {completedTasks}");
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Failed tasks: {tasks.Length - completedTasks}");
    Console.ResetColor();

    Console.WriteLine($"Total: \x1b[32m{completedTasks}\x1b[0m/\x1b[31m{tasks.Length}\x1b[0m (\x1b[34m{Math.Round(completedTasks / (double)tasks.Length, 2) * 100}%\x1b[0m)");
} else {
    var successfulTagLists = new HashSet<int>();

    for (int i = 0; i < TAGLIST_COUNT; i++)
    {
        if (completedTagLists.Contains(i))
            continue;

        if (WriteTagsToDisk(i))
            successfulTagLists.Add(i);
    }

    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"Statistics:");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Completed tasks: {successfulTagLists.Count}");
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Failed tasks: {TAGLIST_COUNT - successfulTagLists.Count}");
    Console.ResetColor();

    Console.WriteLine($"Total: \x1b[32m{successfulTagLists.Count}\x1b[0m/\x1b[31m{TAGLIST_COUNT}\x1b[0m (\x1b[34m{Math.Round(successfulTagLists.Count / (double)TAGLIST_COUNT, 2) * 100}%\x1b[0m)");
}
return;

async Task<bool> WriteTagsToDiskAsync(int page, bool triedBefore = false)
{
    var info = async (string msg) => {
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        await Console.Out.WriteLineAsync($"[{page}] {msg}");
        Console.ResetColor();
    };

    var error = async (string msg) => {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        await Console.Error.WriteLineAsync($"[{page}] {msg}");
        Console.ResetColor();
    };

    var success = async (string msg) => {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        await Console.Out.WriteLineAsync($"[{page}] {msg}");
        Console.ResetColor();
    };

    if (!triedBefore)
        await info($"Starting");
    var outFile = $"pages/{page}.json";
    if (File.Exists(outFile)) {
        await error($"File already exists");
        return false;
    }
    (TagList?, string) res;
    try {
        res = await TagList.GetTagListAsync(API_KEY, USER_ID, page);
    }
    catch (Exception ex) when (ex is TagList.DeserialisationException or HttpRequestException) {
        if (!triedBefore) {
            await error($"Failed to get tags: {ex.Message}");
            await info($"Retrying...");
            return await WriteTagsToDiskAsync(page, true);
        } else {
            await error($"Failed again");
        }

        return false;
    }
    if (res.Item1 == null || res.Item1.Tags == null || res.Item1.Tags.Length == 0)
    {
        await error($"No tags found");
        return false;
    }

    await File.WriteAllTextAsync(outFile, res.Item2);

    await success($"Done!");
    return true;
}

bool WriteTagsToDisk(int page, bool triedBefore = false)
{
    var info = (string msg) => {
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.Out.WriteLine($"[{page}] {msg}");
        Console.ResetColor();
    };

    var error = (string msg) => {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Error.WriteLine($"[{page}] {msg}");
        Console.ResetColor();
    };

    var success = (string msg) => {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.Out.WriteLine($"[{page}] {msg}");
        Console.ResetColor();
    };

    if (!triedBefore)
        info("Starting");
    var outFile = $"pages/{page}.json";
    if (File.Exists(outFile)) {
        error("File already exists");
        return false;
    }
    (TagList?, string) res;
    try {
        res = TagList.GetTagList(API_KEY, USER_ID, page);
    }
    catch (Exception ex) when (ex is TagList.DeserialisationException or HttpRequestException) {
        if (!triedBefore) {
            error($"Failed to get tags: {ex.Message}");
            info("Retrying...");
            return WriteTagsToDisk(page, true);
        } else {
            error("Failed again");
        }

        return false;
    }
    if (res.Item1 == null || res.Item1.Tags == null || res.Item1.Tags.Length == 0)
    {
        error("No tags found");
        return false;
    }

    File.WriteAllText(outFile, res.Item2);
    success("Done!");
    return true;
}

class TagList(TagList.TagAttributes attributes, TagList.Tag[] tags)
{
    public readonly record struct TagAttributes(int Limit, int Offset, int Count);
    public readonly record struct Tag(int ID, string Name, int Count, int Type, int Ambiguous);


    [JsonPropertyName("@attributes")]
    public TagAttributes Attributes { get; } = attributes;

    [JsonPropertyName("tag")]
    public Tag[] Tags { get; } = tags;

    public static readonly JsonSerializerOptions DESERIALISATION_OPTIONS = new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true
    };

    public class DeserialisationException : JsonException
    {
        public string Input { get; }
        public DeserialisationException(string input, JsonException inner) : base("Failed to deserialise", inner) => Input = input;
    }

    public static async Task<(TagList?, string)> GetTagListAsync(string apiKey, string userID, int page = 0)
    {
        var client = new HttpClient();
        var response = await client.GetAsync($"https://gelbooru.com/index.php?page=dapi&s=tag&q=index&json=1&pid={page}&api_key=${apiKey}&user_id=${userID}");
        var json = await response.Content.ReadAsStringAsync();
        try {
            return (JsonSerializer.Deserialize<TagList>(json, DESERIALISATION_OPTIONS), json);
        } catch (JsonException ex) {
            throw new DeserialisationException(json, ex);
        }
    }

    public static (TagList?, string) GetTagList(string apiKey, string userID, int page = 0)
    {
        var client = new HttpClient();
        var response = client.GetAsync($"https://gelbooru.com/index.php?page=dapi&s=tag&q=index&json=1&pid={page}&api_key=${apiKey}&user_id=${userID}").Result;
        var json = response.Content.ReadAsStringAsync().Result;
        try {
            return (JsonSerializer.Deserialize<TagList>(json, DESERIALISATION_OPTIONS), json);
        } catch (JsonException ex) {
            throw new DeserialisationException(json, ex);
        }
    }
}
