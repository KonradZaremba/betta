# Use case: porting an existing plugin (PDF+) to Betta

[PDF+](https://github.com/interopxyz/PdfPlus) by David Mans (MIT) builds PDF documents in Grasshopper: pages, text blocks, tables, charts, images, and Rhino geometry drawn as vector shapes. It is 48 hand-written `GH_Component`s over a PDF engine built on PDFsharp and MigraDoc.

This experiment keeps the engine exactly as it is, replaces the 48 components with Betta methods, and checks that the result looks the same to a Grasshopper user and to a saved `.gh` file. The original remains the real PDF+; this port is a measurement, not a replacement.

The code is in the [`betta-port` branch of KonradZaremba/PdfPlus](https://github.com/KonradZaremba/PdfPlus/tree/betta-port): [`PdfPlus.Betta/`](https://github.com/KonradZaremba/PdfPlus/tree/betta-port/PdfPlus.Betta) (the port) and [`PdfPlus.Betta.Tests/`](https://github.com/KonradZaremba/PdfPlus/tree/betta-port/PdfPlus.Betta.Tests).

## Results

| | PDF+, hand-written | PDF+ on Betta |
|---|---|---|
| Component code | 49 files, 5,709 lines | 7 files, 1,597 lines (**0.28x**) |
| Estimated tokens (characters ÷ 4) | ~54.4k | ~22.4k (**0.41x**) |
| Components | 48 | 48: same IDs, names, toolbar places, inputs, outputs and icons |
| Changes to the engine | | `partial` on six classes, one `#if` (+12/−6 lines) |

Of the port's 1,597 lines, 1,152 are the 48 component methods. The other 445 connect the engine to Betta: viewport preview and bake (moved from the original base class), and the typed wires.

Lines fall further than tokens because what's left is mostly what users read: names, nicknames and descriptions, carried over word for word. What disappears is the code around them: `RegisterInputParams`, `RegisterOutputParams`, `DA.GetData`/`SetData`, and one class per component.

**The port was mostly done by an agent.** Three components (Save Document, Create Page Boundary, Text Block) were ported by hand as examples. One headless Claude Code session (Claude Sonnet 5.5) then ported the other 45 from a single prompt in 10.4 minutes, for $2.15 (119 turns, 71.8k output tokens, 6.92M tokens in all, mostly re-reading the repo).

## How the port is built

1. **The engine compiles unchanged.** `PdfPlus.Betta.csproj` links the original `Classes/` and `Utilities/` source files. Six classes became `partial`. One line, `ChartFrame.Background`, sits behind `#if BETTA`: the published PDFsharp 1.51 lacks it, unlike the custom PDFsharp build the original uses.
2. **PDF+ objects travel on their own wires.** Block, Fragment, DataSet, Shape, Page and Document implement `IBettaValue`, so each is one typed wire instead of being split into one output per property.
3. **Preview and bake come with the objects.** Shape, Page and Document implement `IBettaPreview` and `IBettaBakeable`, using the drawing code the original components shared through their base class.
4. **Generic inputs keep working.** PDF+ inputs accept "anything" (text, a curve, a shape) and convert it with the engine's `TryGetX(IGH_Goo)` helpers. Betta hands a method plain values, so a one-line `Wire.Goo` wraps them back up and the engine's converters run unchanged.
5. **Saved files open.** Each method pins its original `ComponentGuid` (`[GrasshopperMethod(Guid = …)]`) and points `IconResource` at the original PNG.
6. **.NET 7 needs one setup line.** PDFsharp 1.51 writes fonts in Windows-1252, which .NET 7 has only after `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`.

## Tests

`dotnet test PdfPlus.Betta.Tests` runs 12 tests. They need Rhino 8 installed.

- **Port contract (5 tests, no Rhino core).** They read the original components' C# source and compare it with what Betta publishes from the port's attributes. All 48 match on component ID, name, nickname, tab and panel, inputs in order, outputs in order, and icon.
- **PDF output (7 tests).** They start Rhino 8's core inside the test process, with no window and a headless document, call the ported methods the way Betta calls them, save real PDFs and read them back with PDFsharp:
  - text on an A4 page, with the text found in the page;
  - three pages keeping their sizes (A4, Letter, A3 landscape);
  - title and subject written to the PDF;
  - a password that stops the PDF opening without it;
  - Rhino curves and a text shape drawn on a page, with a preview box covering them;
  - Save writing nothing until toggled, and warning about a missing folder;
  - the same result whether an input arrives as a plain value or as Grasshopper goo.

Testing found two bugs, both fixed: Save Document threw on .NET 7 (the missing code page), and PDF+ objects passed through Grasshopper's geometry conversion first, which needs an open Rhino document.

**Not covered yet:** the Grasshopper canvas itself, meaning wiring components by hand, drawing the preview in the viewport, and baking.

## What changed for users

- Optional inputs that PDF+ applied "only if wired" now use a default that means "not set": NaN, −1, empty, or an invalid rectangle. Document Permissions defaults to allowing everything.
- Several integer inputs lost their dropdown of named values (layout mode, units and so on); they still take the same numbers.
- Page Boundaries no longer draws its coloured art, bleed, crop and trim boxes.
- Fast Table Block and Quick Data Set always use the first data set rather than one per solve iteration.
- Set Font, Set Font Preset, Set Graphics and Set Paragraph Format output an untyped element, since they take any PDF+ element.
- Kept from the original: two components named "Document Password" (the options component reuses the name), and an Optional Password input that does nothing.
- Fixed from the original: the Set components no longer fail on input that isn't a PDF+ element, and the dash pattern output is text.

## What it showed about Betta

The first two differences above come from things Betta can't express yet, and the port ran into two more:

- **Was this input wired?** A Betta method sees a default value, not "no input". Optional inputs need a way to tell the two apart, for example `double?` arriving as `null`.
- **Named values for integer inputs.** Grasshopper's `AddNamedValue` dropdown has no attribute yet; an enum parameter could produce it.
- **Toolbar rows.** There's no way to set a component's exposure (primary, secondary, …).
- **Selected-state preview.** `IBettaPreview` doesn't know whether its component is selected, so a selected component draws the same as an unselected one.
