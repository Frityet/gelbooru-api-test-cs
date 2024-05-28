
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

const string API_KEY = "";
const string USER_ID = "";
const int TAGLIST_COUNT = 10098;

var completedTagLists = new HashSet<int>();
var failedTagLists = new HashSet<int>();

if (!Directory.Exists("pages"))
    Directory.CreateDirectory("pages");

//check every file in the pages directory
foreach (var file in Directory.EnumerateFiles("pages"))
{
    var page = int.Parse(Path.GetFileNameWithoutExtension(file));
    completedTagLists.Add(page);
}

var successfulTagLists = new HashSet<int>();

//find the -j flag
int threadCount = 4;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "-j" && i + 1 < args.Length)
    {
        threadCount = Int32.Parse(args[i + 1]);
        break;
    }
}

var tasks = new Task[threadCount]; //max 8 tasks at a time

//each gets TAGLIST_COUNT / 8 pages
for (int i = 0; i < threadCount; i++)
{
    var start = i * (TAGLIST_COUNT / threadCount);
    var end = (i + 1) * (TAGLIST_COUNT / threadCount);

    void fn(int i)
    {
        tasks[i] = Task.Run(async () =>
        {
            using var client = new HttpClient();
            for (int page = start; page < end; page++)
            {
                if (completedTagLists.Contains(page))
                    continue;

                try
                {
                    await WriteTagsToDiskAsync(client, i, page);
                    successfulTagLists.Add(page);
                }
                catch (Exception)
                {
                    failedTagLists.Add(page);
                }
            }
        });
    }
    //so we capture the value of i, no race condition
    fn(i);
}

await Task.WhenAll(tasks);

Console.ForegroundColor = ConsoleColor.DarkYellow;
Console.WriteLine($"Statistics:");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"Completed tasks: {successfulTagLists.Count}");
Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine($"Failed tasks: {TAGLIST_COUNT - successfulTagLists.Count}");
Console.ResetColor();

Console.WriteLine($"Total: \x1b[32m{successfulTagLists.Count}\x1b[0m/\x1b[31m{TAGLIST_COUNT}\x1b[0m (\x1b[34m{Math.Round(successfulTagLists.Count / (double)TAGLIST_COUNT, 2) * 100}%\x1b[0m)");

return;

async Task WriteTagsToDiskAsync(HttpClient client, int id, int page, bool triedBefore = false)
{
    async Task info(string msg)
    {
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        await Console.Out.WriteLineAsync($"[{id} - {page}]\t{msg}");
        Console.ResetColor();
    }

    async Task error(string msg, bool @throw = true)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        await Console.Error.WriteLineAsync($"[{id} - {page}]\t{msg}");
        Console.ResetColor();

        if (@throw)
            throw new Exception(msg);
    }

    async Task success(string msg)
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        await Console.Out.WriteLineAsync($"[{id} - {page}]\t{msg}");
        Console.ResetColor();
    }

    var timer = Stopwatch.StartNew();

    var outFile = $"pages/{page}.json";
    if (File.Exists(outFile))
        await error($"File already exists");

    (TagList?, string) res;
    try {
        res = await TagList.GetTagListAsync(client, API_KEY, USER_ID, page);
    } catch (Exception ex) when (ex is TagList.DeserialisationException or HttpRequestException) {
        if (!triedBefore) {
            await error($"Failed to get tags: {ex.Message}", @throw: false);
            await info($"Retrying...");
            await Task.Delay(1000);
            await WriteTagsToDiskAsync(client, id, page, true);
            return;
        } else {
            await error($"Failed again");
            throw;
        }
    }

    if (res.Item1 == null || res.Item1.Tags == null || res.Item1.Tags.Length == 0)
        await error($"No tags found");


    await File.WriteAllTextAsync(outFile, res.Item2);

    await success($"Downloaded! \x1b[34mTook {timer.ElapsedMilliseconds / 1000.0}s\x1b[0m");
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

    public static async Task<(TagList?, string)> GetTagListAsync(HttpClient client, string apiKey, string userID, int page = 0)
    {
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
