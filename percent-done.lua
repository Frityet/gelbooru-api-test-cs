local lfs = require("lfs")

local PAGE_COUNT = 10098

local count = 0
local finished_pages = {}
for file in lfs.dir("./pages") do
    if file ~= "." and file ~= ".." then
        finished_pages[assert(tonumber(file:match("(.+)%..+")))] = true
        count = count + 1
    end
end


if arg[1] == "--show-missing" then
    for i = 1, PAGE_COUNT do
        if not finished_pages[i] then print(i) end
    end
else
    print(string.format("%d/%d (%.2f%%) downloaded", count, PAGE_COUNT, count/PAGE_COUNT * 100))
end
