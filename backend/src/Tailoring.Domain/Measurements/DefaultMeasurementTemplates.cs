namespace Tailoring.Domain.Measurements;

public static class DefaultMeasurementTemplates
{
    /// <summary>Starter templates created for each new business. Fully editable; add your own for other garments.</summary>
    public static List<MeasurementTemplate> Create(Guid businessId)
    {
        MeasurementTemplate T(string name, params (string key, string label, bool required)[] fields)
        {
            var t = new MeasurementTemplate { BusinessId = businessId, Name = name };
            t.Fields = fields.Select((f, i) => new MeasurementTemplateField
            {
                BusinessId = businessId, TemplateId = t.Id, Key = f.key, Label = f.label, Unit = "cm",
                MinValue = 1, MaxValue = 300, IsRequired = f.required, SortOrder = i
            }).ToList();
            return t;
        }

        return
        [
            T("Shirt", ("neck", "Neck", true), ("chest", "Chest", true), ("waist", "Waist", false), ("shoulder", "Shoulder", true),
              ("sleeve_length", "Sleeve length", true), ("shirt_length", "Shirt length", true), ("bicep", "Bicep", false), ("wrist", "Wrist", false)),
            T("Trousers", ("waist", "Waist", true), ("hips", "Hips", true), ("rise", "Rise", false), ("inseam", "Inseam", true),
              ("outseam", "Outseam", true), ("thigh", "Thigh", false), ("knee", "Knee", false), ("bottom_opening", "Bottom opening", false)),
            T("Jacket / Suit", ("chest", "Chest", true), ("waist", "Waist", true), ("hips", "Hips", false), ("shoulder", "Shoulder", true),
              ("sleeve_length", "Sleeve length", true), ("jacket_length", "Jacket length", true), ("back_width", "Back width", false), ("bicep", "Bicep", false)),
            T("Dress / Gown", ("bust", "Bust", true), ("waist", "Waist", true), ("hips", "Hips", true), ("shoulder", "Shoulder", false),
              ("shoulder_to_waist", "Shoulder to waist", false), ("dress_length", "Dress length", true), ("sleeve_length", "Sleeve length", false)),
            T("Skirt", ("waist", "Waist", true), ("hips", "Hips", true), ("skirt_length", "Skirt length", true)),
            T("Native attire", ("chest", "Chest", true), ("waist", "Waist", false), ("hips", "Hips", false), ("shoulder", "Shoulder", true),
              ("sleeve_length", "Sleeve length", false), ("top_length", "Top length", true), ("trouser_waist", "Trouser waist", false), ("trouser_length", "Trouser length", false)),
        ];
    }
}
