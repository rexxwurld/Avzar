using System.Security.Cryptography;
using System.Text;

namespace Tailoring.Domain.Storefront;

public sealed record SwitchChange(Guid SectionId, string Type, string? FromVariant, string? ToVariant, string Page);

/// <param name="Token">Hash of exactly this plan. The apply call must send it back, so what the owner confirmed is what happens.</param>
public sealed record SwitchPlan(string FromTemplate, string ToTemplate, IReadOnlyList<SwitchChange> Kept, IReadOnlyList<SwitchChange> Converted,
    IReadOnlyList<SwitchChange> Archived, IReadOnlyList<SwitchChange> Restored, string Token)
{
    /// <summary>True when anything about the layout will look different. The UI must show a warning before applying.</summary>
    public bool NeedsConfirmation => Converted.Count > 0 || Archived.Count > 0;
}

/// <summary>
/// Moves a document to another template. Content is NEVER deleted: a section the new template cannot show is moved to
/// the archive, and archived sections the new template CAN show are put back on the home page. Theme, branding,
/// navigation and page content stay as they are unless the caller asks for the new template's look.
/// </summary>
public static class TemplateSwitcher
{
    public static SwitchPlan Plan(StorefrontDocument doc, TemplateDef target)
    {
        var kept = new List<SwitchChange>(); var converted = new List<SwitchChange>(); var archived = new List<SwitchChange>(); var restored = new List<SwitchChange>();

        foreach (var page in doc.Pages)
            foreach (var s in page.Sections)
            {
                if (!target.Supported.TryGetValue(s.Type, out var variants)) archived.Add(new(s.Id, s.Type, s.Variant, null, page.Title));
                else if (variants.Contains(s.Variant)) kept.Add(new(s.Id, s.Type, s.Variant, s.Variant, page.Title));
                else converted.Add(new(s.Id, s.Type, s.Variant, variants[0], page.Title));
            }
        var home = doc.Pages.First(p => p.Kind == "home");
        foreach (var s in doc.Archive.Where(s => target.Supported.ContainsKey(s.Type)))
        {
            var variants = target.Supported[s.Type];
            restored.Add(new(s.Id, s.Type, s.Variant, variants.Contains(s.Variant) ? s.Variant : variants[0], home.Title));
        }

        var canonical = string.Join('|', new[] { doc.TemplateKey, target.Key }
            .Concat(kept.Select(x => $"k{x.SectionId}")).Concat(converted.Select(x => $"c{x.SectionId}>{x.ToVariant}"))
            .Concat(archived.Select(x => $"a{x.SectionId}")).Concat(restored.Select(x => $"r{x.SectionId}>{x.ToVariant}")));
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()[..32];
        return new SwitchPlan(doc.TemplateKey, target.Key, kept, converted, archived, restored, token);
    }

    /// <param name="useTemplateTheme">Also take the new template's theme preset and clear colour/font overrides. Off by default: a switch does not touch the owner's look.</param>
    public static StorefrontDocument Apply(StorefrontDocument doc, TemplateDef target, bool useTemplateTheme)
    {
        var result = StorefrontJson.Clone(doc);
        result.TemplateKey = target.Key;

        foreach (var page in result.Pages)
        {
            var stay = new List<SectionDoc>();
            foreach (var s in page.Sections)
            {
                if (!target.Supported.TryGetValue(s.Type, out var variants)) { result.Archive.Add(s); continue; }
                if (!variants.Contains(s.Variant)) s.Variant = variants[0];
                stay.Add(s);
            }
            page.Sections = stay;
        }

        var home = result.Pages.First(p => p.Kind == "home");
        var back = result.Archive.Where(s => target.Supported.ContainsKey(s.Type)).ToList();
        foreach (var s in back)
        {
            var variants = target.Supported[s.Type];
            if (!variants.Contains(s.Variant)) s.Variant = variants[0];
            result.Archive.Remove(s);
            home.Sections.Add(s);
        }

        if (useTemplateTheme) result.Theme = new ThemeSettings { Preset = target.DefaultPreset };
        return result;
    }
}
