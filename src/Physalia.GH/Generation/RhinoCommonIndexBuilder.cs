// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Physalia.Core.Api;

namespace Physalia.GH.Generation;

/// <summary>
/// Builds an <see cref="ApiIndex"/> of the public RhinoCommon API by reflecting over the loaded
/// <c>RhinoCommon.dll</c> and merging in the prose from the paired <c>RhinoCommon.xml</c>
/// documentation file that ships beside it.
///
/// <para>Reflection is the authoritative source of the callable surface and exact signatures — it
/// sees every public member and knows the return type, static/instance, and parameter types the XML
/// omits. The XML supplies the human descriptions reflection lacks. Each member is matched to its XML
/// entry by the .NET documentation-comment ID (e.g. <c>M:Rhino.Geometry.Brep.CreateFromLoft(...)</c>),
/// reconstructed from the reflected member. Members whose ID cannot be matched (or when the XML file
/// is absent) still appear in the index with their signature, just without prose.</para>
///
/// <para>The build is comparatively expensive (a full reflection pass plus an XML parse), so it runs
/// once and is cached for the process lifetime via <see cref="Index"/>. Because building inside a
/// solve would freeze Grasshopper, the consuming tool node runs asynchronously and touches
/// <see cref="Index"/> off the solve thread, paying the cost on the first model call only.</para>
/// </summary>
internal static class RhinoCommonIndexBuilder
{
    private static readonly Lazy<ApiIndex> LazyIndex =
        new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(void)] = "void",
        [typeof(object)] = "object",
        [typeof(string)] = "string",
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
    };

    /// <summary>
    /// Gets the cached RhinoCommon API index, building it on first access. The build runs on the
    /// calling thread, so call this off the Grasshopper solve thread.
    /// </summary>
    public static ApiIndex Index => LazyIndex.Value;

    private static ApiIndex Build()
    {
        Assembly assembly;
        try
        {
            assembly = typeof(Rhino.RhinoApp).Assembly;
        }
        catch
        {
            return new ApiIndex(Array.Empty<ApiMember>());
        }

        IReadOnlyDictionary<string, MemberDoc> docs = LoadXmlDocs(assembly);
        var members = new List<ApiMember>();

        foreach (Type type in SafeGetExportedTypes(assembly))
        {
            if (type.IsSpecialName)
            {
                continue;
            }

            try
            {
                CollectType(type, docs, members);
            }
            catch
            {
                // A single type that fails to reflect (e.g. a missing dependency) must not abort the
                // whole build — skip it and keep indexing the rest.
            }
        }

        return new ApiIndex(members);
    }

    private static IEnumerable<Type> SafeGetExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is { IsPublic: true })!;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static void CollectType(
        Type type,
        IReadOnlyDictionary<string, MemberDoc> docs,
        List<ApiMember> output)
    {
        const BindingFlags memberFlags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        const BindingFlags ctorFlags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        string typeFull = FullDisplayName(type);
        string typeShort = FriendlyTypeShort(type);
        string typeIdName = TypeNameForMemberId(type);

        // The type itself, so a query for the type name surfaces it directly.
        MemberDoc typeDoc = Lookup(docs, "T:" + typeIdName);
        output.Add(new ApiMember(
            ApiMemberKind.Type, typeFull, typeShort, TypeSignature(type),
            type is { IsAbstract: true, IsSealed: true }, typeDoc.Summary, typeDoc.Returns, typeDoc.Parameters));

        foreach (ConstructorInfo ctor in type.GetConstructors(ctorFlags))
        {
            MemberDoc doc = Lookup(docs, CtorDocId(typeIdName, ctor));
            output.Add(new ApiMember(
                ApiMemberKind.Constructor, typeFull, typeShort, CtorSignature(typeShort, ctor),
                false, doc.Summary, doc.Returns, doc.Parameters));
        }

        foreach (MethodInfo method in type.GetMethods(memberFlags))
        {
            if (method.IsSpecialName)
            {
                continue; // property/event accessors and operators
            }

            MemberDoc doc = Lookup(docs, MethodDocId(typeIdName, method));
            output.Add(new ApiMember(
                ApiMemberKind.Method, typeFull, method.Name, MethodSignature(method),
                method.IsStatic, doc.Summary, doc.Returns, doc.Parameters));
        }

        foreach (PropertyInfo property in type.GetProperties(memberFlags))
        {
            MemberDoc doc = Lookup(docs, PropertyDocId(typeIdName, property));
            bool isStatic = (property.GetMethod ?? property.SetMethod)?.IsStatic ?? false;
            output.Add(new ApiMember(
                ApiMemberKind.Property, typeFull, property.Name, PropertySignature(property),
                isStatic, doc.Summary, doc.Returns, doc.Parameters));
        }

        foreach (FieldInfo field in type.GetFields(memberFlags))
        {
            if (field.IsSpecialName)
            {
                continue; // e.g. an enum's backing value__ field
            }

            MemberDoc doc = Lookup(docs, "F:" + typeIdName + "." + field.Name);
            output.Add(new ApiMember(
                ApiMemberKind.Field, typeFull, field.Name, FieldSignature(field),
                field.IsStatic, doc.Summary, doc.Returns, doc.Parameters));
        }

        foreach (EventInfo evt in type.GetEvents(memberFlags))
        {
            MemberDoc doc = Lookup(docs, "E:" + typeIdName + "." + evt.Name);
            bool isStatic = evt.AddMethod?.IsStatic ?? false;
            output.Add(new ApiMember(
                ApiMemberKind.Event, typeFull, evt.Name, EventSignature(evt),
                isStatic, doc.Summary, doc.Returns, doc.Parameters));
        }
    }

    // ----- Signature formatting (reflection -> readable C#) -----

    private static string TypeSignature(Type type)
    {
        string kind = type.IsEnum ? "enum"
            : type.IsInterface ? "interface"
            : typeof(Delegate).IsAssignableFrom(type) ? "delegate"
            : type.IsValueType ? "struct"
            : "class";
        return kind + " " + FullDisplayName(type);
    }

    private static string CtorSignature(string typeShort, ConstructorInfo ctor) =>
        typeShort + "(" + FormatParams(ctor.GetParameters()) + ")";

    private static string MethodSignature(MethodInfo method)
    {
        string mods = method.IsStatic ? "static " : string.Empty;
        string ret = FriendlyTypeShort(method.ReturnType);
        string generics = method.IsGenericMethodDefinition
            ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">"
            : string.Empty;
        return $"{mods}{ret} {method.Name}{generics}({FormatParams(method.GetParameters())})";
    }

    private static string PropertySignature(PropertyInfo property)
    {
        bool isStatic = (property.GetMethod ?? property.SetMethod)?.IsStatic ?? false;
        string mods = isStatic ? "static " : string.Empty;
        string type = FriendlyTypeShort(property.PropertyType);
        ParameterInfo[] indexer = property.GetIndexParameters();
        string name = indexer.Length > 0 ? "this[" + FormatParams(indexer) + "]" : property.Name;
        string accessors = (property.CanRead ? "get; " : string.Empty) + (property.CanWrite ? "set; " : string.Empty);
        return $"{mods}{type} {name} {{ {accessors}}}";
    }

    private static string FieldSignature(FieldInfo field)
    {
        string mods = field.IsLiteral ? "const " : field.IsStatic ? "static " : string.Empty;
        return $"{mods}{FriendlyTypeShort(field.FieldType)} {field.Name}";
    }

    private static string EventSignature(EventInfo evt)
    {
        string mods = (evt.AddMethod?.IsStatic ?? false) ? "static " : string.Empty;
        string handler = evt.EventHandlerType is null ? "EventHandler" : FriendlyTypeShort(evt.EventHandlerType);
        return $"{mods}event {handler} {evt.Name}";
    }

    private static string FormatParams(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(FormatParam));

    private static string FormatParam(ParameterInfo parameter)
    {
        Type type = parameter.ParameterType;
        var prefix = new StringBuilder();
        if (type.IsByRef)
        {
            prefix.Append(parameter.IsOut ? "out " : "ref ");
            type = type.GetElementType()!;
        }

        if (parameter.IsDefined(typeof(ParamArrayAttribute), false))
        {
            prefix.Append("params ");
        }

        string suffix = parameter is { IsOptional: true, HasDefaultValue: true }
            ? " = " + FormatDefault(parameter.DefaultValue)
            : string.Empty;

        return $"{prefix}{FriendlyTypeShort(type)} {parameter.Name ?? "arg"}{suffix}";
    }

    private static string FormatDefault(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "null",
    };

    private static string FriendlyTypeShort(Type type)
    {
        if (type.IsByRef)
        {
            return FriendlyTypeShort(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            int rank = type.GetArrayRank();
            return FriendlyTypeShort(type.GetElementType()!) + "[" + new string(',', rank - 1) + "]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return FriendlyTypeShort(nullable) + "?";
        }

        if (type.IsGenericType)
        {
            string baseName = StripArity(type.Name);
            string args = string.Join(", ", type.GetGenericArguments().Select(FriendlyTypeShort));
            return $"{baseName}<{args}>";
        }

        return Keywords.TryGetValue(type, out string? keyword) ? keyword : type.Name;
    }

    private static string FullDisplayName(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        string name = StripArity(type.Name);
        string container = type.IsNested
            ? FullDisplayName(type.DeclaringType!)
            : type.Namespace ?? string.Empty;
        string full = string.IsNullOrEmpty(container) ? name : container + "." + name;

        if (type.IsGenericType)
        {
            string args = string.Join(", ", type.GetGenericArguments().Select(FriendlyTypeShort));
            return $"{full}<{args}>";
        }

        return full;
    }

    // ----- XML documentation-comment ID reconstruction -----

    private static string MethodDocId(string typeIdName, MethodInfo method)
    {
        string id = "M:" + typeIdName + "." + method.Name;
        if (method.IsGenericMethodDefinition)
        {
            id += "``" + method.GetGenericArguments().Length;
        }

        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length > 0 ? id + "(" + ParamIds(parameters) + ")" : id;
    }

    private static string CtorDocId(string typeIdName, ConstructorInfo ctor)
    {
        string id = "M:" + typeIdName + ".#ctor";
        ParameterInfo[] parameters = ctor.GetParameters();
        return parameters.Length > 0 ? id + "(" + ParamIds(parameters) + ")" : id;
    }

    private static string PropertyDocId(string typeIdName, PropertyInfo property)
    {
        string id = "P:" + typeIdName + "." + property.Name;
        ParameterInfo[] indexer = property.GetIndexParameters();
        return indexer.Length > 0 ? id + "(" + ParamIds(indexer) + ")" : id;
    }

    private static string ParamIds(ParameterInfo[] parameters) =>
        string.Join(",", parameters.Select(p => TypeDocId(p.ParameterType)));

    // The declaring-type portion of a member ID keeps generic arity backticks and joins nested types
    // with '.', e.g. "System.Collections.Generic.List`1".
    private static string TypeNameForMemberId(Type type)
    {
        if (type.IsNested)
        {
            return TypeNameForMemberId(type.DeclaringType!) + "." + type.Name;
        }

        return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;
    }

    // The documentation-comment encoding of a type when it appears in a parameter list.
    private static string TypeDocId(Type type)
    {
        if (type.IsByRef)
        {
            return TypeDocId(type.GetElementType()!) + "@";
        }

        if (type.IsPointer)
        {
            return TypeDocId(type.GetElementType()!) + "*";
        }

        if (type.IsArray)
        {
            Type element = type.GetElementType()!;
            int rank = type.GetArrayRank();
            if (rank == 1)
            {
                return TypeDocId(element) + "[]";
            }

            string dims = string.Join(",", Enumerable.Repeat("0:", rank));
            return TypeDocId(element) + "[" + dims + "]";
        }

        if (type.IsGenericParameter)
        {
            // Method-level type parameters use a double backtick, type-level a single backtick.
            string tick = type.DeclaringMethod is not null ? "``" : "`";
            return tick + type.GenericParameterPosition;
        }

        if (type.IsGenericType)
        {
            string baseName = TypeNameForMemberId(type.GetGenericTypeDefinition());
            int tick = baseName.IndexOf('`');
            if (tick >= 0)
            {
                baseName = baseName.Substring(0, tick);
            }

            string args = string.Join(",", type.GetGenericArguments().Select(TypeDocId));
            return $"{baseName}{{{args}}}";
        }

        return TypeNameForMemberId(type);
    }

    // ----- XML documentation parsing -----

    private static IReadOnlyDictionary<string, MemberDoc> LoadXmlDocs(Assembly assembly)
    {
        var result = new Dictionary<string, MemberDoc>(StringComparer.Ordinal);

        string location = assembly.Location;
        if (string.IsNullOrEmpty(location))
        {
            return result;
        }

        // Resolve the doc file as the sibling .xml of the loaded assembly — platform-agnostic, since
        // it derives from the assembly's own runtime location rather than a hard-coded path.
        //   Windows: C:\Program Files\Rhino 8\System\RhinoCommon.dll -> ...\RhinoCommon.xml (ships here).
        //   Mac (TODO verify): RhinoCommon.dll lives inside the .app bundle
        //     (/Applications/Rhino 8.app/Contents/Frameworks/.../RhinoCommon.framework/.../RhinoCommon.dll);
        //     this resolves the sibling .xml automatically IF Rhino ships RhinoCommon.xml there. It is
        //     unconfirmed that the Mac install includes the doc XML beside the dll — verify on a Mac
        //     Rhino 8. If it is absent, the index still builds with exact signatures, just no prose
        //     summaries; the fallback would be to locate the Mac doc XML or bundle a copy in Files/.
        string xmlPath = Path.ChangeExtension(location, ".xml");
        if (!File.Exists(xmlPath))
        {
            return result;
        }

        try
        {
            XDocument document = XDocument.Load(xmlPath);
            foreach (XElement member in document.Descendants("member"))
            {
                string? id = member.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                string summary = CleanText(member.Element("summary"));
                string returns = CleanText(member.Element("returns"));
                List<ApiParamDoc> parameters = member.Elements("param")
                    .Select(p => new ApiParamDoc(p.Attribute("name")?.Value ?? string.Empty, CleanText(p)))
                    .Where(p => p.Name.Length > 0)
                    .ToList();

                result[id!] = new MemberDoc(summary, returns, parameters);
            }
        }
        catch
        {
            // A malformed or unreadable XML file degrades to an index without prose, not a failure.
        }

        return result;
    }

    private static string CleanText(XElement? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendNodeText(element, builder);
        return WhitespaceRun.Replace(builder.ToString(), " ").Trim();
    }

    private static void AppendNodeText(XElement element, StringBuilder builder)
    {
        foreach (XNode node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement child:
                    string local = child.Name.LocalName;
                    if (local is "see" or "seealso" or "paramref" or "typeparamref")
                    {
                        string reference = child.Attribute("cref")?.Value
                            ?? child.Attribute("name")?.Value
                            ?? string.Empty;
                        builder.Append(ShortenCref(reference));
                    }
                    else
                    {
                        AppendNodeText(child, builder);
                    }

                    break;
            }
        }
    }

    private static string ShortenCref(string cref)
    {
        int colon = cref.IndexOf(':');
        if (colon >= 0)
        {
            cref = cref.Substring(colon + 1);
        }

        int paren = cref.IndexOf('(');
        if (paren >= 0)
        {
            cref = cref.Substring(0, paren);
        }

        int dot = cref.LastIndexOf('.');
        return dot >= 0 ? cref.Substring(dot + 1) : cref;
    }

    private static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick >= 0 ? name.Substring(0, tick) : name;
    }

    private static MemberDoc Lookup(IReadOnlyDictionary<string, MemberDoc> docs, string id) =>
        docs.TryGetValue(id, out MemberDoc? doc) ? doc : MemberDoc.Empty;

    private sealed record MemberDoc(string Summary, string Returns, IReadOnlyList<ApiParamDoc> Parameters)
    {
        public static readonly MemberDoc Empty =
            new(string.Empty, string.Empty, Array.Empty<ApiParamDoc>());
    }
}
