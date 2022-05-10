
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Serialization;
using PrevueGuide;
using PrevueGuide.SDLWrappers;
using XmlTv.Model;
using static SDL2.SDL;
using static SDL2.SDL_image;
using static SDL2.SDL_ttf;

const int windowWidth = 716;
const int windowHeight = 436;

const int standardRowHeight = 56;
const int firstColumnWidth = 172;
const int secondColumnWidth = 172;
const int thirdColumnWidth = 208;

const string databaseFilename = "listings.db";

var frameTimeList = new List<long>();

var regenerateGridTextures = false;
var channelsToRender = 15;

var data = new PrevueGuide.Core.Data.SQLite.SQLiteListingsData(databaseFilename);

var channelLineUpStopwatch = Stopwatch.StartNew();
var channelLineUp = await data.GetChannelLineup();
Console.WriteLine($"Channel line-up loaded. {channelLineUp.Count()} channels found in {channelLineUpStopwatch.ElapsedMilliseconds} ms.");

var nowBlock = PrevueGuide.Core.Utilities.Time.ClampToPreviousHalfHour(DateTime.Now);
var nowBlockEnd = nowBlock.AddMinutes(90);
var channelListingsStopwatch = Stopwatch.StartNew();
var channelListings = await data.GetChannelListings(nowBlock, nowBlockEnd);
Console.WriteLine($"Channel listings loaded. {channelListings.Count()} listings found in {channelListingsStopwatch.ElapsedMilliseconds} ms.");

const string FontNamePrevueGrid = nameof(FontNamePrevueGrid);
const string FontNameABPreview = nameof(FontNameABPreview);
const string FontNameDINBold = nameof(FontNameDINBold);
const string FontNameComicNeueBold = nameof(FontNameComicNeueBold);

var fontConfigurationMap = new Dictionary<string, FontConfiguration>
{
    {
        FontNamePrevueGrid, new FontConfiguration
        {
            Filename = "assets/PrevueGrid.ttf",
            PointSize = 25,
            XOffset = 0,
            YOffset = 0
        }
    },
    {
        FontNameABPreview, new FontConfiguration
        {
            Filename = "assets/ab-preview.ttf",
            PointSize = 23,
            XOffset = -1, // -4,
            YOffset = -7 // -11
        }
    },
    {
        FontNameDINBold, new FontConfiguration // Hollywood
        {
            Filename = "/Users/rj/Library/Fonts/DINBd___.ttf",
            PointSize = 25,
            XOffset = -1,
            YOffset = -7
        }
    },
    {
        FontNameComicNeueBold, new FontConfiguration
        {
            Filename = "/Users/rj/Library/Fonts/ComicNeue_Bold.otf",
            PointSize = 25,
            XOffset = 0,
            YOffset = 2
        }
    }
};

var selectedFont = fontConfigurationMap[FontNamePrevueGrid];

IntPtr window;
IntPtr renderer;
IntPtr openedTtfFont;

DateTime time = DateTime.UnixEpoch;

Texture? timeTexture = null;
Texture? channelFrameTexture = null;
Texture? clockFrameTexture = null;
Texture? timeboxFrameTexture = null;
Texture? timeboxLastFrameTexture = null;
Texture? timeboxFrameOneTime = null;
Texture? timeboxFrameTwoTime = null;
Texture? timeboxFrameThreeTime = null;
Texture? guideDoubleArrowLeft = null;
Texture? guideDoubleArrowRight = null;

// Texture? bigFrameTexture = null;

Texture? columnOneOrTwo = null;
Texture? columnThree = null;
Texture? columnOneAndTwo = null;
Texture? columnTwoAndThree = null;
Texture? columnOneTwoAndThree = null;

var listingChannelTextureMap = new Dictionary<string, (Texture? Line1, Texture? Line2)>();
var listingTextTextureMap = new Dictionary<string, List<(Texture? Line1, Texture? Line2, DateTime StartTime, DateTime EndTime)>>();

const int numberOfFrameTimesToCapture = 60;

var scale = 1;
var running = true;
var showFrameRate = false;
var limitFps = false;

var gridTextYellow = new SDL_Color { a = 255, r = 203, g = 209, b = 0 };
var gridTextWhite = new SDL_Color { a = 255, r = 170, g = 170, b = 170 };
var clockBackgroundColor = new SDL_Color { a = 255, r = 34, g = 41, b = 141 };
var gridTestRed = new SDL_Color { a = 255, r = 192, g = 0, b = 0 };
var gridDefaultBlue = new SDL_Color { a = 255, r = 3, g = 0, b = 88 };

var gridOffset = 0;
var scrollingTest = 0;

var fontMap = new Dictionary<char, int>();

Setup();

while (running)
{
    PollEvents();
    Render();
}

CleanUp();

void GenerateBigText()
{
    Console.WriteLine("Generating textures:");

    for (var i = 0; i < channelsToRender + 5; i++)
    {
        var channel = channelLineUp.ElementAtOrDefault(i);
        if (channel != null)
        {
            var listings = channelListings.Where(cl => cl.ChannelId == channel.Id);

            var listingList = new List<(Texture? Line1, Texture? Line2, DateTime StartTime, DateTime EndTime)>();

            if (listings.Any())
            {
                foreach (var listing in listings)
                {
                    if (listing == null)
                    {
                        foreach (var s in channelListings.Where(listing => listing.ChannelId == channel.Id))
                        {
                            Console.WriteLine($"{s.Title} [{s.StartTime} -> {s.EndTime}]");
                        }
                    }

                    // var textLength = CalculateLineWidths(channelListings.FirstOrDefault()?.Title, firstColumnWidth,
                    //    new Dictionary<int, int>());

                    var firstLine = listing?.Title ?? " ";
                    var line1 = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont, firstLine,
                        gridTextWhite, scale));

                    var secondLine = " ";
                    var line2 = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont, secondLine,
                        gridTextWhite, scale));

                    listingList.Add((line1, line2, listing.StartTime, listing.EndTime));
                }

                listingTextTextureMap.Add(channel.Id, listingList);

                var channelLine1 = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont,
                    channel.ChannelNumber,
                    gridTextYellow, scale));
                var channelLine2 = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont,
                    channel.CallSign,
                    gridTextYellow, scale));

                listingChannelTextureMap.Add(channel.Id, (channelLine1, channelLine2));
            }
        }
    }
}

async Task ProcessXmlTvFile(string filename)
{
    try
    {
        Console.WriteLine($"Received file: {filename}");

        var xmlReaderSettings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore
        };

        await using var fileStream = new FileStream(filename, FileMode.Open);
        using var xmlReader = XmlReader.Create(fileStream, xmlReaderSettings);
        var tv = (Tv)new XmlSerializer(typeof(Tv)).Deserialize(xmlReader)!;

        Console.WriteLine("Importing channel listing data...");

        var numberOfChannels = 0;
        if (tv.Channel != null)
        {
            foreach (var channel in tv.Channel)
            {
                await data.AddChannelToLineup(channel.SourceName, channel.ChannelNumber, channel.CallSign);
                numberOfChannels++;
            }
        }
        Console.WriteLine($"Imported {numberOfChannels} channels.");

        var numberOfPrograms = 0;
        if (tv.Programme != null)
        {
            var queue = new Queue<(string, string, string, DateTime, DateTime)>();

            foreach (var programme in tv.Programme)
            {
                var title = programme.Title.First().Text;
                var description = programme.Desc.FirstOrDefault()?.Text ?? "";
                queue.Enqueue((programme.SourceName, title, description,
                    DateTime.ParseExact(programme.Start, "yyyyMMddHHmmss zzz", DateTimeFormatInfo.CurrentInfo,
                        DateTimeStyles.AssumeLocal).ToUniversalTime(),
                    DateTime.ParseExact(programme.Stop, "yyyyMMddHHmmss zzz", DateTimeFormatInfo.CurrentInfo,
                        DateTimeStyles.AssumeLocal).ToUniversalTime()));
                numberOfPrograms++;
            }

            while (queue.Any())
            {
                var list = new List<(string, string, string, DateTime, DateTime)>();

                for (var i = 0; i < 15; i++)
                {
                    if (!queue.Any())
                        break;

                    list.Add(queue.Dequeue());
                }

                await data.AddChannelListing(list);
            }
        }

        Console.WriteLine($"Imported {numberOfPrograms} programs.");
        Console.WriteLine("Channel list imported.");

        regenerateGridTextures = true;
        Console.WriteLine("Prepared for regeneration.");
    }
    catch (Exception e)
    {
        Console.WriteLine($"Encountered exception in rendering of XMLTV: {e.Message} @ {e.StackTrace}");
    }
}

IEnumerable<string> CalculateLineWidths(string targetString, int defaultLineWidth, Dictionary<int, int> specifiedLineWidths)
{
    var currentLineLength = 0;
    var currentLineNumber = 1;
    var currentLine = string.Empty;
    var renderedLines = new List<string>();

    var lineWidth = specifiedLineWidths.ContainsKey(currentLineNumber)
        ? specifiedLineWidths[currentLineNumber]
        : defaultLineWidth;

    foreach (var component in targetString.Split(' '))
    {
        var componentLength = component.ToCharArray().Sum(c => fontMap[c]);
        var paddedComponentLength = (string.IsNullOrWhiteSpace(currentLine) ? 0 : fontMap[' ']) + componentLength;

        if (currentLineLength + paddedComponentLength > lineWidth)
        {
            if (!string.IsNullOrWhiteSpace(currentLine))
            {
                renderedLines.Add(currentLine);
                currentLine = component;
                currentLineLength = componentLength;

                currentLineNumber++;
                lineWidth = specifiedLineWidths.ContainsKey(currentLineNumber)
                    ? specifiedLineWidths[currentLineNumber]
                    : defaultLineWidth;
            }
            else
            {
                // We have to split the line in the middle somewhere.
                var chars = component.ToCharArray();
                var componentSubLength = 0;
                var chunk = string.Empty;

                foreach (var targetChar in chars)
                {
                    var glyphWidth = fontMap[targetChar];
                    var newSubLength = componentSubLength + glyphWidth;

                    if (newSubLength > lineWidth)
                    {
                        renderedLines.Add(chunk);
                        chunk = string.Empty;
                        componentSubLength = 0;

                        currentLineNumber++;
                        lineWidth = specifiedLineWidths.ContainsKey(currentLineNumber)
                            ? specifiedLineWidths[currentLineNumber]
                            : defaultLineWidth;
                    }

                    chunk = $"{chunk}{targetChar}";
                    componentSubLength += glyphWidth;
                }

                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    var padding = string.IsNullOrWhiteSpace(currentLine) ? string.Empty : " ";
                    currentLine = $"{currentLine}{padding}{chunk}";
                    currentLineLength += componentSubLength;
                }
            }
        }
        else
        {
            var padding = string.IsNullOrWhiteSpace(currentLine) ? string.Empty : " ";
            currentLine = $"{currentLine}{padding}{component}";
            currentLineLength += paddedComponentLength;
        }
    }

    if (!string.IsNullOrWhiteSpace(currentLine))
    {
        renderedLines.Add(currentLine);
    }

    return renderedLines;
}

// Setup all of the SDL resources we'll need to display a window.
void Setup()
{
    // Initializes SDL.
    if (SDL_Init(SDL_INIT_VIDEO) < 0)
    {
        Console.WriteLine($"There was an issue initializing SDL. {SDL_GetError()}");
    }

    _ = TTF_Init();
    _ = IMG_Init(IMG_InitFlags.IMG_INIT_PNG);

    // Create a new window given a title, size, and passes it a flag indicating it should be shown.
    window = SDL_CreateWindow(
        "",
        SDL_WINDOWPOS_UNDEFINED,
        SDL_WINDOWPOS_UNDEFINED,
        windowWidth,
        windowHeight,
        SDL_WindowFlags.SDL_WINDOW_SHOWN | SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI);

    SDL_GL_GetDrawableSize(window, out var windowSizeW, out var windowSizeH);
    Console.WriteLine($"Drawable Size: {windowSizeW} x {windowSizeH}");
    scale = windowSizeH / windowHeight;
    Console.WriteLine($"Scale: {scale}x");

    if (window == IntPtr.Zero)
    {
        Console.WriteLine($"There was an issue creating the window. {SDL_GetError()}");
    }

    // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
    renderer = SDL_CreateRenderer(
        window,
        -1,
        SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);

    if (renderer == IntPtr.Zero)
    {
        Console.WriteLine($"There was an issue creating the renderer. {SDL_GetError()}");
    }

    _ = SDL_SetRenderDrawBlendMode(renderer, SDL_BlendMode.SDL_BLENDMODE_BLEND);

    openedTtfFont = TTF_OpenFont(selectedFont.Filename, selectedFont.PointSize * scale);

    // Generate a font width map.
    for (var i = 0; i < 256; i++)
    {
        var c = (char)i;
        _ = TTF_SizeText(openedTtfFont, $"{c}", out var w, out _);
        fontMap[c] = w;
    }

    clockFrameTexture = new Texture(Generators.GenerateFrame(renderer, 144, 34, clockBackgroundColor, scale));
    timeboxFrameTexture = new Texture(renderer, "assets/timebox_frame_2x_smooth.png");
    timeboxLastFrameTexture = new Texture(renderer, "assets/timebox_last_frame_2x_smooth.png");
    channelFrameTexture = new Texture(renderer, "assets/channel_frame_2x_smooth.png");
    guideDoubleArrowLeft = new Texture(renderer, "assets/guide_double_arrow_left_2x_smooth.png");
    guideDoubleArrowRight = new Texture(renderer, "assets/guide_double_arrow_right_2x_smooth.png");

    timeboxFrameOneTime = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont,
        nowBlock.ToString("h:mm tt"), gridTextYellow, scale));
    timeboxFrameTwoTime = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont,
        nowBlock.AddMinutes(30).ToString("h:mm tt"), gridTextYellow, scale));
    timeboxFrameThreeTime = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont,
        nowBlock.AddMinutes(60).ToString("h:mm tt"), gridTextYellow, scale));

    columnOneOrTwo = new Texture(Generators.GenerateFrame(renderer, firstColumnWidth, standardRowHeight, gridDefaultBlue, scale));
    columnThree = new Texture(Generators.GenerateFrame(renderer, thirdColumnWidth, standardRowHeight, gridDefaultBlue, scale));
    columnOneAndTwo = new Texture(Generators.GenerateFrame(renderer, firstColumnWidth * 2, standardRowHeight, gridDefaultBlue, scale));
    columnTwoAndThree = new Texture(Generators.GenerateFrame(renderer, firstColumnWidth + thirdColumnWidth, standardRowHeight, gridDefaultBlue, scale));
    columnOneTwoAndThree = new Texture(Generators.GenerateFrame(renderer, (firstColumnWidth * 2) + thirdColumnWidth, standardRowHeight, gridDefaultBlue, scale));

    GenerateBigText();
}

// Checks to see if there are any events to be processed.
void PollEvents()
{
    // Check to see if there are any events and continue to do so until the queue is empty.
    while (SDL_PollEvent(out var sdlEvent) == 1)
    {
        if (sdlEvent.type == SDL_EventType.SDL_QUIT)
            running = false;
        else if (sdlEvent.type == SDL_EventType.SDL_WINDOWEVENT)
        {
            Console.WriteLine($"SDL Window Event: {sdlEvent.window.windowEvent}");
            // Interested in:
            // SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_LOST
            // SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_GAINED
            // Would be nice to pause the renderer so it doens't use 100% CPU when the window isn't focused
        }
        else if (sdlEvent.type == SDL_EventType.SDL_DROPFILE)
        {
            var filename = Marshal.PtrToStringAuto(sdlEvent.drop.file);
            Task.Run(() => ProcessXmlTvFile(filename).Wait());
        }
        else if (sdlEvent.type == SDL_EventType.SDL_KEYDOWN)
        {
            switch (sdlEvent.key.keysym.sym)
            {
                case SDL_Keycode.SDLK_f:
                    showFrameRate = !showFrameRate;
                    break;
                case SDL_Keycode.SDLK_l:
                    limitFps = !limitFps;
                    break;
                case SDL_Keycode.SDLK_q:
                    running = false;
                    break;
                case SDL_Keycode.SDLK_UP:
                    gridOffset -= (2 * scale);
                    if (gridOffset < 0)
                        gridOffset = 0;
                    break;
                case SDL_Keycode.SDLK_DOWN:
                    gridOffset += (2 * scale);
                    break;
            }
        }
    }
}

IntPtr GenerateGridTexture()
{
    // Cheap and cheating.
    var now = DateTime.Now;
    if (time.Second != now.Second)
    {
        time = now;

        timeTexture?.Dispose();
        timeTexture = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont, now.ToString("h:mm:ss"),
            gridTextWhite, scale));
    }

    if (regenerateGridTextures)
    {
        GenerateBigText();
        regenerateGridTextures = false;
    }

    var gridTexture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGBA8888,
                   (int)SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, windowWidth * scale,
                   windowHeight * scale);
    _ = SDL_SetTextureBlendMode(gridTexture, SDL_BlendMode.SDL_BLENDMODE_BLEND);

    // Switch to the texture for rendering.
    using (_ = new RenderingTarget(renderer, gridTexture))
    {
        // Blank out the grid texture with blue
        _ = SDL_SetRenderDrawColor(renderer, 4, 0, 89, 255);
        _ = SDL_RenderClear(renderer);

        const int frameX = 152; // Start first program column
        const int frameY = 206; // Start below frame.

        var horizontalOffset = 62;
        const int verticalOffset = 7;

        // Quick guess. 110 frames for a full grid push @ 112 (2x) / 56 (1x) height.
        // That means roughly 2 frames per pixel going up.
        scrollingTest += 1;
        if (scrollingTest >= standardRowHeight * channelsToRender * scale)
            scrollingTest = 0;
        var testingOffset = (scrollingTest / 2);

        // Draw listings data.
        for (var i = 0; i < channelsToRender; i++)
        {
            var channel = channelLineUp.ElementAtOrDefault(i);
            if (channel != null)
            {
                var listingTextureMap = listingTextTextureMap[channel.Id];

                foreach (var listing in listingTextureMap)
                {
                    var textLine1 = listing.Line1;
                    var textLine2 = listing.Line2;

                    var initialColumn = 1;
                    var frameOffset = 0;

                    var remainingDuration = (listing.EndTime - nowBlock);
                    var frameColumn = 1; // FIguring out the block position may be easier if i calculate that at import.

                    Texture? frameTexture = null;

                    if (remainingDuration.TotalMinutes > 60)
                    {
                        frameTexture = columnOneTwoAndThree;
                    }
                    else if (remainingDuration.TotalMinutes > 30)
                    {
                        frameTexture = columnOneAndTwo;
                    }
                    else
                    {
                        frameTexture = columnOneOrTwo;
                    }

                    _ = SDL_QueryTexture(frameTexture.SdlTexture, out _, out _, out var bfWidth, out var bfHeight);
                    var bfDstRect = new SDL_Rect
                    {
                        h = bfHeight, w = bfWidth, x = frameX * scale,
                        y = ((frameY - testingOffset + (i * standardRowHeight)) * scale)
                    };
                    _ = SDL_RenderCopy(renderer, frameTexture.SdlTexture, IntPtr.Zero, ref bfDstRect);

                    var textLeftMargin = 0;
                    var textRightMargin = 0;

                    if (listing.StartTime < nowBlock)
                    {
                        _ = SDL_QueryTexture(guideDoubleArrowLeft.SdlTexture, out _, out _, out var gdalWidth,
                            out var gdalHeight);
                        var gdalDstRect = new SDL_Rect
                        {
                            h = gdalHeight, w = gdalWidth, x = (frameX + 5) * scale,
                            y = (frameY + 5 - testingOffset + (i * standardRowHeight)) * scale
                        };
                        _ = SDL_RenderCopy(renderer, guideDoubleArrowLeft.SdlTexture, IntPtr.Zero, ref gdalDstRect);
                        textLeftMargin = (gdalWidth / scale);
                    }

                    if (listing.EndTime > nowBlockEnd)
                    {
                        _ = SDL_QueryTexture(guideDoubleArrowRight.SdlTexture, out _, out _, out var gdalWidth,
                            out var gdalHeight);
                        var gdalDstRect = new SDL_Rect
                        {
                            h = gdalHeight, w = gdalWidth,
                            x = (frameX + 525) *
                                scale, // Calculate this from the frame width? I think I did the math wrong initially.
                            y = (frameY + 5 - testingOffset + (i * standardRowHeight)) * scale
                        };
                        _ = SDL_RenderCopy(renderer, guideDoubleArrowRight.SdlTexture, IntPtr.Zero, ref gdalDstRect);
                        textRightMargin = (gdalWidth / scale);
                    }

                    _ = SDL_QueryTexture(textLine1.SdlTexture, out _, out _, out var bftWidth, out var bftHeight);
                    var bftDstRect = new SDL_Rect
                    {
                        h = bftHeight,
                        w = bftWidth,
                        x = (frameX + 5 + textLeftMargin + selectedFont.XOffset) * scale,
                        y = (frameY + 5 - testingOffset + (i * standardRowHeight) + selectedFont.YOffset) * scale
                    };
                    _ = SDL_RenderCopy(renderer, textLine1.SdlTexture, IntPtr.Zero, ref bftDstRect);

                    _ = SDL_QueryTexture(textLine2.SdlTexture, out _, out _, out var bftWidth2, out var bftHeight2);
                    var bftDstRect2 = new SDL_Rect
                    {
                        h = bftHeight2, w = bftWidth2, x = (frameX + 5 + textLeftMargin + selectedFont.XOffset) * scale,
                        y = (frameY + 5 + 24 - testingOffset + (i * standardRowHeight) + selectedFont.YOffset) * scale
                    };
                    _ = SDL_RenderCopy(renderer, textLine2.SdlTexture, IntPtr.Zero, ref bftDstRect2);
                }
            }
        }

        // Draw the channel frames.
        {
            for (var i = 0; i < channelsToRender; i++)
            {
                {
                    _ = SDL_QueryTexture(channelFrameTexture.SdlTexture, out _, out _, out var w, out var h);
                    var dstRect1 = new SDL_Rect
                    {
                        h = h,
                        w = w,
                        x = 8 * scale,
                        y = ((frameY - testingOffset + (i * standardRowHeight)) * scale)
                    };
                    _ = SDL_RenderCopy(renderer, channelFrameTexture.SdlTexture, IntPtr.Zero, ref dstRect1);
                }

                var channel = channelLineUp.ElementAtOrDefault(i);
                if (channel != null)
                {
                    var channelTextures = listingChannelTextureMap[channel.Id];

                    {
                        _ = SDL_QueryTexture(channelTextures.Line1.SdlTexture, out _, out _, out var w, out var h);
                        var wOffset = ((90 - (w / scale) / 2) + 8);
                        var dstRect1 = new SDL_Rect
                        {
                            h = h,
                            w = w,
                            x = (wOffset + selectedFont.XOffset) * scale,
                            y = ((frameY - testingOffset + (i * standardRowHeight) + 5 + selectedFont.YOffset) * scale)
                        };
                        _ = SDL_RenderCopy(renderer, channelTextures.Line1.SdlTexture, IntPtr.Zero, ref dstRect1);
                    }

                    {
                        _ = SDL_QueryTexture(channelTextures.Line2.SdlTexture, out _, out _, out var w, out var h);
                        var wOffset = ((90 - (w / scale) / 2) + 8);
                        var dstRect1 = new SDL_Rect
                        {
                            h = h,
                            w = w,
                            x = (wOffset + selectedFont.XOffset) * scale,
                            y = ((frameY - testingOffset + (i * standardRowHeight) + 29 + selectedFont.YOffset) * scale)
                        };
                        _ = SDL_RenderCopy(renderer, channelTextures.Line2.SdlTexture, IntPtr.Zero, ref dstRect1);
                    }
                }
            }
        }

        // Draw the clock frame.
        _ = SDL_QueryTexture(clockFrameTexture.SdlTexture, out _, out _, out var clockFrameWidth, out var clockFrameHeight);
        var clockFrameDstRect = new SDL_Rect { h = clockFrameHeight, w = clockFrameWidth, x = 8 * scale, y = 0 };
        _ = SDL_RenderCopy(renderer, clockFrameTexture.SdlTexture, IntPtr.Zero, ref clockFrameDstRect);

        // First two time boxes.
        {
            _ = SDL_QueryTexture(timeboxFrameTexture.SdlTexture, out uint _, out int _, out int tbw, out int tbh);
            var timeFrameRect1 = new SDL_Rect { h = tbh, w = tbw, x = 152 * scale, y = 0 };
            _ = SDL_RenderCopy(renderer, timeboxFrameTexture.SdlTexture, IntPtr.Zero, ref timeFrameRect1);
            var timeFrameRect2 = new SDL_Rect { h = tbh, w = tbw, x = 324 * scale, y = 0 };
            _ = SDL_RenderCopy(renderer, timeboxFrameTexture.SdlTexture, IntPtr.Zero, ref timeFrameRect2);

            // Last one.
            _ = SDL_QueryTexture(timeboxLastFrameTexture.SdlTexture, out _, out _, out var tblw, out var tblh);
            var timeFrameRect3 = new SDL_Rect { h = tblh, w = tblw, x = 496 * scale, y = 0 };
            _ = SDL_RenderCopy(renderer, timeboxLastFrameTexture.SdlTexture, IntPtr.Zero, ref timeFrameRect3);
        }

        // Times.
        {
            _ = SDL_QueryTexture(timeboxFrameOneTime.SdlTexture, out uint _, out int _, out int tw1, out int th1);
            var timeRect1 = new SDL_Rect
            {
                h = th1,
                w = tw1,
                x = (192 + selectedFont.XOffset) * scale,
                y = (verticalOffset - 1 + selectedFont.YOffset) * scale
            };
            _ = SDL_RenderCopy(renderer, timeboxFrameOneTime.SdlTexture, IntPtr.Zero, ref timeRect1);

            _ = SDL_QueryTexture(timeboxFrameTwoTime.SdlTexture, out uint _, out int _, out int tw2, out int th2);
            var timeRect2 = new SDL_Rect
            {
                h = th2,
                w = tw2,
                x = (364 + selectedFont.XOffset) * scale,
                y = (verticalOffset - 1 + selectedFont.YOffset) * scale
            };
            _ = SDL_RenderCopy(renderer, timeboxFrameTwoTime.SdlTexture, IntPtr.Zero, ref timeRect2);

            _ = SDL_QueryTexture(timeboxFrameThreeTime.SdlTexture, out uint _, out int _, out int tw3, out int th3);
            var timeRect3 = new SDL_Rect
            {
                h = th3,
                w = tw3,
                x = (536 + selectedFont.XOffset) * scale,
                y = (verticalOffset - 1 + selectedFont.YOffset) * scale
            };
            _ = SDL_RenderCopy(renderer, timeboxFrameThreeTime.SdlTexture, IntPtr.Zero, ref timeRect3);
        }

        if (DateTime.Now.Hour is 00 or >= 10)
            horizontalOffset -= 12;

        _ = SDL_QueryTexture(timeTexture.SdlTexture, out _, out _, out var timeWidth, out var timeHeight);
        var timeDstRect = new SDL_Rect
            {
                h = timeHeight,
                w = timeWidth,
                x = (horizontalOffset - 1 + selectedFont.XOffset) * scale,
                y = (verticalOffset - 1 + selectedFont.YOffset) * scale
            };
        _ = SDL_RenderCopy(renderer, timeTexture.SdlTexture, IntPtr.Zero, ref timeDstRect);
    }

    return gridTexture;
}

// Renders to the window.
void Render()
{
    var frameDrawStopWatch = Stopwatch.StartNew();
    var frameDelayStopWatch = Stopwatch.StartNew();

    _ = SDL_SetRenderDrawColor(renderer, 255, 0, 255, 255);
    _ = SDL_RenderClear(renderer);

    // Generate the grid
    using var gridTexture = new Texture(GenerateGridTexture());

    // Render the grid.
    _ = SDL_QueryTexture(gridTexture.SdlTexture, out _, out _, out var gridTextureWidth, out var gridTextureHeight);
    var gridDstRect = new SDL_Rect { h = gridTextureHeight, w = gridTextureWidth, x = 0, y = 227 * scale + gridOffset };
    _ = SDL_RenderCopy(renderer, gridTexture.SdlTexture, IntPtr.Zero, ref gridDstRect);

    // Draw FPS.
    if (showFrameRate && frameTimeList.Any())
    {
        // Generate average FPS.
        var averageFrameTime = frameTimeList.Average();
        var averageFps = 1000 / averageFrameTime;

        var fpsTexture = Generators.GenerateDropShadowText(renderer, openedTtfFont,
            $"FPS: {averageFps:F}", gridTextYellow, scale);

        _ = SDL_QueryTexture(fpsTexture, out _, out _, out var fpsTextureWidth, out var fpsTextureHeight);
        var fpsDstRect = new SDL_Rect { h = fpsTextureHeight, w = fpsTextureWidth, x = (windowWidth - 180) * scale, y = (6 * scale) };
        _ = SDL_RenderCopy(renderer, fpsTexture, IntPtr.Zero, ref fpsDstRect);
        SDL_DestroyTexture(fpsTexture);
    }

    // Switches out the currently presented render surface with the one we just did work on.
    SDL_RenderPresent(renderer);

    frameDelayStopWatch.Stop();

    const int targetFps = 30;
    if (limitFps)
    {
        const int targetDuration = 1000 / targetFps;
        var duration = (targetDuration - frameDelayStopWatch.ElapsedMilliseconds);

        if (duration > 0)
            SDL_Delay((uint)duration);
    }

    frameTimeList.Add(frameDrawStopWatch.ElapsedMilliseconds);

    while (frameTimeList.Count > numberOfFrameTimesToCapture)
    {
        frameTimeList.RemoveAt(0);
    }
}

// Clean up the resources that were created.
void CleanUp()
{
    timeboxFrameOneTime.Dispose();
    timeboxFrameTwoTime.Dispose();
    timeboxFrameThreeTime.Dispose();
    timeTexture?.Dispose();
    channelFrameTexture?.Dispose();
    clockFrameTexture?.Dispose();
    timeboxFrameTexture?.Dispose();
    timeboxLastFrameTexture?.Dispose();
    guideDoubleArrowLeft?.Dispose();

    foreach (var k in listingTextTextureMap.Keys)
    {
        foreach (var sublisting in listingTextTextureMap[k])
        {
            sublisting.Line1.Dispose();
            sublisting.Line2.Dispose();
        }
    }

    foreach (var t in listingChannelTextureMap.Keys)
    {
        listingChannelTextureMap[t].Line1.Dispose();
        listingChannelTextureMap[t].Line2.Dispose();
    }

    SDL_DestroyRenderer(renderer);
    SDL_DestroyWindow(window);

    data.Dispose();

    TTF_CloseFont(openedTtfFont);
    TTF_Quit();
    SDL_Quit();
}

record Channel
{
    public string ChannelNumber { get; init; }
    public string CallSign { get; init; }
}

record FontConfiguration
{
    public string Filename { get; init; }
    public int PointSize { get; init; }
    public int XOffset { get; init; } = 0;
    public int YOffset { get; init; } = 0;
}
