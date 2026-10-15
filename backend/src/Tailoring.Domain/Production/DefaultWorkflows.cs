namespace Tailoring.Domain.Production;

public static class DefaultWorkflows
{
    /// <summary>
    /// The workflow from the spec, as an editable starting point:
    /// Measurements Verified -> Materials Prepared -> Cutting -> Sewing -> Finishing -> Quality Control -> Fitting
    /// -> (Alterations, only if requested) -> Final Quality Check -> Ready for Collection.
    /// "Order Confirmed" and "Delivered" are order-level states, not tasks.
    /// </summary>
    public static (ProductionWorkflow Workflow, List<StageDependency> Dependencies) Create(Guid businessId)
    {
        var wf = new ProductionWorkflow
        {
            BusinessId = businessId, Name = "Standard garment", IsDefault = true,
            Description = "Default made-to-measure flow. Edit or duplicate it for your own process."
        };

        var order = 0;
        ProductionStage S(string key, string name, int? minutes, string? skill, string[] checks, bool conditional = false, string? instructions = null)
        {
            var stage = new ProductionStage
            {
                BusinessId = businessId, WorkflowId = wf.Id, Key = key, Name = name, SortOrder = order++,
                DefaultMinutes = minutes, RequiredSkill = skill, CompletionChecks = checks.ToList(),
                IsConditional = conditional, Instructions = instructions
            };
            wf.Stages.Add(stage);
            return stage;
        }

        var verify = S("measurements_verified", "Measurements Verified", 15, null,
            ["Measurements match the order snapshot"], instructions: "Compare the order's measurement snapshot with the customer's request.");
        var materials = S("materials_prepared", "Materials Prepared", 30, null,
            ["Fabric and trims set aside", "Quantities checked"]);
        var cutting = S("cutting", "Cutting", 60, "cutting", ["Pattern pieces counted"]);
        var sewing = S("sewing", "Sewing", 120, "sewing", []);
        var finishing = S("finishing", "Finishing", 45, "finishing", ["Loose threads trimmed", "Pressed"]);
        var qc = S("quality_control", "Quality Control", 20, "quality control", ["Seams and measurements inspected"]);
        var fitting = S("fitting", "Fitting", 30, null, []);
        var alterations = S("alterations", "Alterations", 45, "sewing", [], conditional: true);
        var finalQc = S("final_quality_check", "Final Quality Check", 20, "quality control", ["Final inspection passed"]);
        var ready = S("ready_for_collection", "Ready for Collection", 10, null, ["Garment packaged and labelled"]);

        StageDependency D(ProductionStage stage, ProductionStage on) => new()
            { BusinessId = businessId, StageId = stage.Id, DependsOnStageId = on.Id };

        var deps = new List<StageDependency>
        {
            D(materials, verify), D(cutting, materials), D(sewing, cutting), D(finishing, sewing),
            D(qc, finishing), D(fitting, qc), D(alterations, fitting),
            D(finalQc, fitting), D(finalQc, alterations), D(ready, finalQc)
        };
        return (wf, deps);
    }
}
