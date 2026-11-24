# Installation

Magic IndexedDB 2 targets .NET 10 and runs in Blazor applications in the browser. If your application still targets .NET 8, use an earlier compatible package.

## Install the package

Install [`Magic.IndexedDb`](https://www.nuget.org/packages/Magic.IndexedDb/) from NuGet:

```bash
dotnet add package Magic.IndexedDb
```

Review the [release history](https://github.com/magiccodingman/Magic.IndexedDb/releases) before upgrading an existing application.

## Register the service

Register Magic IndexedDB in `Program.cs`. The second argument controls development-time validation and JavaScript diagnostic logging.

For standalone Blazor WebAssembly:

```csharp
using Magic.IndexedDb;

builder.Services.AddMagicBlazorDB(
    BlazorInteropMode.WASM,
    builder.HostEnvironment.IsDevelopment());
```

For a Blazor application whose JavaScript interop travels over SignalR:

```csharp
builder.Services.AddMagicBlazorDB(
    BlazorInteropMode.SignalR,
    builder.Environment.IsDevelopment());
```

The built-in limits are 15 MiB for `WASM` and 31 KiB for `SignalR`. They leave room beneath the corresponding default transport limits. You may instead specify an explicit byte limit:

```csharp
long messageLimit = 2 * 1024 * 1024;

builder.Services.AddMagicBlazorDB(
    messageLimit,
    builder.HostEnvironment.IsDevelopment());
```

A larger limit can reduce interop calls but increases peak message memory. Progressive query streaming removes the need to fit an entire result set into a single interop message, so choose a limit appropriate to the transport rather than the largest possible value.

## Use the service in Razor components

Add the namespace to `_Imports.razor`:

```razor
@using Magic.IndexedDb
```

Inject the scoped service in a page or component:

```razor
@inject IMagicIndexedDb MagicDb
```

Magic IndexedDB imports its JavaScript module lazily. The browser database is created or opened when the application first uses the service for a query.

In a prerendered server application, browser JavaScript is not available during prerendering. Start database work after the component becomes interactive, such as from an appropriate `OnAfterRenderAsync(firstRender)` path, rather than unconditionally during prerendered initialization.

## Debug mode

When `isDebug` is `true`, Magic IndexedDB performs reflection-based table validation during registration and enables its JavaScript debug messages. Production registration skips that validation and suppresses informational JavaScript logs; errors and warnings remain visible.

Keep validation enabled during development. If trimming or ahead-of-time compilation changes which types reflection can discover, validate the published application as well as the development build.

## Continue

Next, [define your repository and table schema](schema.md).
