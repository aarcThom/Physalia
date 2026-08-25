// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Physalia.GH.Generation;

/// <summary>
/// Points PDFtoImage and SkiaSharp at their native binaries explicitly, instead of trusting the
/// ambient probing that a normal .NET application gets for free.
///
/// <para><b>Why this class has to exist.</b> NuGet lays PDFium and Skia down as
/// <c>runtimes/&lt;rid&gt;/native/…</c> beside the managed shims, and the runtime finds them by
/// consulting the application's <c>.deps.json</c> through its host or an
/// <c>AssemblyDependencyResolver</c>. A Grasshopper plug-in gets neither: Grasshopper loads a
/// <c>.gha</c> with a plain <c>Assembly.LoadFrom</c> into the default load context, so the
/// dependency resolver that would map "pdfium" onto a RID-specific file is never in the picture.
/// The default P/Invoke probe then falls back to the OS search path — which contains Rhino's own
/// directory and the system directories, and does not contain ours. The symptom is a
/// <see cref="DllNotFoundException"/> at the first render, in Rhino only, from a build that is
/// perfectly healthy on the command line.</para>
///
/// <para>Registering a resolver removes the guesswork: the RID subfolder is picked here, from the
/// platform and architecture actually running, and the file is loaded by absolute path. Both
/// packages are denylisted from the ILRepack merge for the same reason — see the comment on
/// <c>RepackDenyList</c> in Physalia.GH.csproj.</para>
/// </summary>
internal static class PdfNativeLibrary
{
    private static readonly object Gate = new();
    private static bool _installed;
    private static string? _failure;

    /// <summary>
    /// Installs the resolvers once per process. Safe to call from anywhere, any number of times;
    /// callers do so immediately before their first render rather than at plug-in load, so a
    /// canvas that never touches a PDF never touches these assemblies either.
    /// </summary>
    /// <returns>
    /// Null when the resolvers are in place, or a human-readable reason when this platform has no
    /// binaries shipped for it.
    /// </returns>
    internal static string? Install()
    {
        lock (Gate)
        {
            if (_installed)
            {
                return _failure;
            }

            _installed = true;

            string? rid = RuntimeIdentifier();
            if (rid is null)
            {
                _failure =
                    "PDF page rendering is not available on this platform: no PDFium build ships " +
                    $"for {RuntimeInformation.OSDescription} on {RuntimeInformation.ProcessArchitecture}. " +
                    "Text extraction still works.";
                return _failure;
            }

            try
            {
                // Resolvers are per-assembly, and the two shims are separate assemblies, so both
                // need registering. Types are named indirectly so a missing package surfaces here
                // as one clear message rather than a TypeLoadException at an arbitrary call site.
                Register(typeof(PDFtoImage.Conversion).Assembly, rid);
                Register(typeof(SkiaSharp.SKBitmap).Assembly, rid);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or TypeLoadException)
            {
                _failure = "PDF page rendering could not be initialised: " + ex.Message;
            }

            return _failure;
        }
    }

    /// <summary>
    /// Registers a DllImport resolver for one assembly that maps a bare library name onto the file
    /// shipped for this RID, falling back to the default behaviour for anything unrecognised.
    /// </summary>
    /// <param name="assembly">The assembly whose P/Invokes should be resolved.</param>
    /// <param name="rid">The runtime identifier subfolder to load from.</param>
    private static void Register(Assembly assembly, string rid) =>
        NativeLibrary.SetDllImportResolver(assembly, (name, asm, path) =>
        {
            string? file = Locate(name, rid);
            if (file is not null && NativeLibrary.TryLoad(file, out IntPtr handle))
            {
                return handle;
            }

            // Zero means "carry on with the default probe". A plug-in folder that has been
            // flattened by a packaging step still works, because the default probe covers it.
            return IntPtr.Zero;
        });

    /// <summary>
    /// Finds a native library on disk, preferring the RID subfolder the packages ship and
    /// accepting a flattened copy beside the assembly.
    /// </summary>
    /// <param name="name">The library name as written in the DllImport.</param>
    /// <param name="rid">The runtime identifier subfolder.</param>
    /// <returns>The absolute path, or null when nothing matching exists.</returns>
    private static string? Locate(string name, string rid)
    {
        string? root = Path.GetDirectoryName(typeof(PdfNativeLibrary).Assembly.Location);
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        foreach (string candidate in FileNames(name))
        {
            // The two packages do not agree on how specific the folder is: PDFium files its macOS
            // build under osx-arm64/osx-x64, while SkiaSharp ships one universal binary under a
            // bare "osx". Trying the architecture-qualified name first and the bare OS name second
            // covers both without needing to know which library is being asked for.
            foreach (string folder in RidFolders(rid))
            {
                string ridPath = Path.Combine(root, "runtimes", folder, "native", candidate);
                if (File.Exists(ridPath))
                {
                    return ridPath;
                }
            }

            string flatPath = Path.Combine(root, candidate);
            if (File.Exists(flatPath))
            {
                return flatPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Expands a runtime identifier into the folder names to search, most specific first.
    /// </summary>
    /// <param name="rid">The runtime identifier, such as <c>osx-arm64</c>.</param>
    /// <returns>The folders to try.</returns>
    private static string[] RidFolders(string rid)
    {
        int dash = rid.IndexOf('-');
        return dash > 0 ? new[] { rid, rid[..dash] } : new[] { rid };
    }

    /// <summary>
    /// Expands a DllImport name into the file names it could have on this platform. The shims
    /// write the name unadorned, and each platform decorates it differently.
    /// </summary>
    /// <param name="name">The library name as written in the DllImport.</param>
    /// <returns>Candidate file names, most likely first.</returns>
    private static string[] FileNames(string name)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new[] { name + ".dll", name };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new[] { name + ".dylib", "lib" + name + ".dylib", name };
        }

        return new[] { name + ".so", "lib" + name + ".so", name };
    }

    /// <summary>
    /// Resolves the runtime identifier folder the shipped natives are filed under.
    /// </summary>
    /// <returns>The RID, or null on a platform nothing ships for.</returns>
    private static string? RuntimeIdentifier()
    {
        string? os =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : null;

        if (os is null)
        {
            return null;
        }

        string? arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => null,
        };

        return arch is null ? null : os + "-" + arch;
    }
}
