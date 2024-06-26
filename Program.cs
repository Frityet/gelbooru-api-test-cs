﻿
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const string API_KEY = "";
const string USER_ID = "";
const int TAGLIST_COUNT = 10098;

var completedTagLists = new HashSet<int>();
var failedTagLists = new HashSet<int>();
var lastCompleted = Stopwatch.StartNew();

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

int remaining = TAGLIST_COUNT - completedTagLists.Count;
//each gets TAGLIST_COUNT / 8 pages
for (int i = 0; i < threadCount; i++)
{
    var start = i * (TAGLIST_COUNT / threadCount);
    var end = (i + 1) * (TAGLIST_COUNT / threadCount);

    void fn(int i)
    {
        tasks[i-2] = Task.Run(async () =>
        {
            using var client = new HttpClient();
            var timer = Stopwatch.StartNew();
            for (int page = start; page < end; page++)
            {
                if (completedTagLists.Contains(page))
                    continue;

                try
                {
                    await WriteTagsToDiskAsync(timer, client, i, page);
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
    fn(i+2);
}

Console.Clear();

Console.CursorVisible = false;
var task = Task.WhenAll(tasks);
while (!task.IsCompleted)
{
    var elapsed = lastCompleted.ElapsedMilliseconds / 100.0;
    Console.SetCursorPosition(0, 0);
    ClearCurrentConsoleLine();
    var pcntDone = successfulTagLists.Count / (double)remaining;
    Console.Write($"{successfulTagLists.Count}/{remaining} ({Math.Round(pcntDone * 100)}%) completed\t{String.Format("{0:0.00}s {1, -64}", elapsed / 10, NumberToBlocks(elapsed))}");

    Console.SetCursorPosition(0, 1);
    ClearCurrentConsoleLine();
    Console.Write($"[{new string('█', (int)(pcntDone * 50))}{new string(' ', 50 - (int)(pcntDone * 50))}]");

    await Task.Delay(100);
}
Console.CursorVisible = true;


Console.ForegroundColor = ConsoleColor.DarkYellow;
Console.WriteLine($"Statistics:");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"Completed tasks: {successfulTagLists.Count}");
Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine($"Failed tasks: {TAGLIST_COUNT - successfulTagLists.Count}");
Console.ResetColor();

// Console.WriteLine($"Total: \x1b[32m{successfulTagLists.Count}\x1b[0m/\x1b[31m{TAGLIST_COUNT}\x1b[0m (\x1b[34m{String.Format("{0:0.00}, {1}", Math.Round(successfulTagLists.Count / (double)TAGLIST_COUNT, 2) * 100)}%\x1b[0m)");

return;

void ClearCurrentConsoleLine()
{
    int currentLineCursor = Console.CursorTop;
    Console.SetCursorPosition(0, Console.CursorTop);
    Console.Write(new string(' ', Console.WindowWidth));
    Console.SetCursorPosition(0, currentLineCursor);
}

//displays double as unicode blocks, so 1.5 is one block, and then a half block
string NumberToBlocks(double num)
{
    var whole = Math.Floor(num);
    var frac = num - whole;
    return new string('█', (int)whole) + (frac > 0 ? new string('▌', (int)(frac * 2)) : "");
}


async Task WriteTagsToDiskAsync(Stopwatch timer, HttpClient client, int id, int page, bool triedBefore = false)
{
    // void error(string msg, bool @throw = true)
    // {
    //     Console.ForegroundColor = ConsoleColor.DarkRed;
    //     Console.SetCursorPosition(0, id);
    //     ClearCurrentConsoleLine();
    //     Console.Error.Write($"[{id} - {page}]\t{msg}");
    //     Console.ResetColor();

    //     if (@throw)
    //         throw new Exception(msg);
    // }

    // void success(string msg)
    // {
    //     Console.ForegroundColor = ConsoleColor.DarkGreen;
    //     Console.SetCursorPosition(0, id);
    //     ClearCurrentConsoleLine();
    //     Console.Out.Write($"[{id} - {page}]\t{msg}");
	// Console.Out.Flush();
    //     Console.ResetColor();

    // }

    async Task error(string msg, bool @throw = true)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.SetCursorPosition(0, id);
        ClearCurrentConsoleLine();
        await Console.Error.WriteAsync($"[{id} - {page}]\t{msg}");
        await Console.Error.FlushAsync();
        Console.ResetColor();

        if (@throw)
            throw new Exception(msg);
    }

    async Task success(string msg)
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.SetCursorPosition(0, id);
        ClearCurrentConsoleLine();
        await Console.Out.WriteAsync($"[{id} - {page}]\t{msg}");
        await Console.Out.FlushAsync();
        Console.ResetColor();
    }

    var outFile = $"pages/{page}.json";
    if (File.Exists(outFile))
        await error($"File already exists");

    (TagList?, string) res;
    try {
        res = await TagList.GetTagListAsync(client, API_KEY, USER_ID, page);
    } catch (Exception ex) when (ex is TagList.DeserialisationException or HttpRequestException) {
        if (!triedBefore) {
            // error($"Failed to get tags: {ex.Message}, retrying...", @throw: false);
            await Task.Delay(1000);
            await WriteTagsToDiskAsync(timer, client, id, page, true);
            return;
        } else {
            await error($"Failed again, took {timer.ElapsedMilliseconds / 1000.0}s");
            throw; //unreachable
        }
    }

    if (res.Item1 == null || res.Item1.Tags == null || res.Item1.Tags.Length == 0)
        await error($"No tags found");


    await File.WriteAllTextAsync(outFile, res.Item2);

    var tElapsed = timer.ElapsedMilliseconds / 100.0;
    await success(String.Format("Downloaded! \x1b[34mTime elapsed: {0, -100}", String.Format("{0:0.00}s {1}", tElapsed / 10, NumberToBlocks(tElapsed))));

    lastCompleted.Restart();
    timer.Restart();
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
