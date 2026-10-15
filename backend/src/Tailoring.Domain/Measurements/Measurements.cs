using Tailoring.Domain.Common;

namespace Tailoring.Domain.Measurements;

/// <summary>A garment category's measurement form (e.g. "Shirt"). Businesses can create their own.</summary>
public class MeasurementTemplate : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public List<MeasurementTemplateField> Fields { get; set; } = new();
}

public class MeasurementTemplateField : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid TemplateId { get; set; }
    /// <summary>Stable machine key, e.g. "sleeve_length". Lowercase letters, digits, underscore.</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Unit { get; set; } = "cm";
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>A customer's measurement profile for one template, e.g. "Wedding suit 2026". History lives in revisions.</summary>
public class CustomerMeasurementSet : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid TemplateId { get; set; }
    public string Label { get; set; } = "";
    public int CurrentRevision { get; set; }
    public List<MeasurementRevision> Revisions { get; set; } = new();
}

/// <summary>Append-only. Values carry their own label and unit so history stays readable if the template changes.</summary>
public class MeasurementRevision : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid SetId { get; set; }
    public int RevisionNumber { get; set; }
    /// <summary>JSON array of <see cref="MeasurementValue"/>.</summary>
    public string ValuesJson { get; set; } = "[]";
    public string? Notes { get; set; }
    public Guid? RecordedByUserId { get; set; }
}

public sealed record MeasurementValue(string Key, string Label, string Unit, decimal Value);
