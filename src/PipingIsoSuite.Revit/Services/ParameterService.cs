using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;

namespace PipingIsoSuite.Revit.Services;

public static class ParameterService
{
    /// <summary>Id of the project parameter bound under <paramref name="name"/>, or InvalidElementId.</summary>
    public static ElementId FindParameterId(Document doc, string name)
    {
        var it = doc.ParameterBindings.ForwardIterator();
        while (it.MoveNext())
            if (it.Key is InternalDefinition def && def.Name == name)
                return def.Id;
        return ElementId.InvalidElementId;
    }

    /// <summary>
    /// Ensures a text instance parameter exists on pipe categories, creating it as a shared parameter
    /// with a GUID derived from its name so every project gets the same definition (tags/schedules work).
    /// Must run inside a transaction.
    /// </summary>
    public static ElementId EnsureTextParameter(Document doc, string name)
    {
        var existing = FindParameterId(doc, name);
        if (existing != ElementId.InvalidElementId) return existing;

        var app = doc.Application;
        var original = app.SharedParametersFilename;
        var temp = Path.Combine(Path.GetTempPath(), "PipingIsoSuite_SharedParameters.txt");
        if (!File.Exists(temp)) File.WriteAllText(temp, "");
        try
        {
            app.SharedParametersFilename = temp;
            var file = app.OpenSharedParameterFile();
            var group = file.Groups.get_Item("PipingIsoSuite") ?? file.Groups.Create("PipingIsoSuite");
            var definition = group.Definitions.get_Item(name) ?? group.Definitions.Create(
                new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text)
                {
                    GUID = new Guid(MD5.HashData(Encoding.UTF8.GetBytes("PipingIsoSuite:" + name))),
                    Description = "Piping line number (Piping ISO Suite)",
                });

            var categories = new CategorySet();
            foreach (var bic in PipeGraphBuilder.PipeCategories)
                categories.Insert(Category.GetCategory(doc, bic));
            doc.ParameterBindings.Insert(definition, app.Create.NewInstanceBinding(categories), GroupTypeId.IdentityData);
        }
        finally
        {
            app.SharedParametersFilename = original;
        }
        return FindParameterId(doc, name);
    }

    /// <summary>Copies a parameter value between elements when storage types match and the target is writable.</summary>
    public static bool CopyValue(Parameter source, Parameter? target)
    {
        if (target is null || target.IsReadOnly || target.StorageType != source.StorageType) return false;
        return source.StorageType switch
        {
            StorageType.String => target.Set(source.AsString() ?? ""),
            StorageType.Double => target.Set(source.AsDouble()),
            StorageType.Integer => target.Set(source.AsInteger()),
            StorageType.ElementId => target.Set(source.AsElementId()),
            _ => false,
        };
    }
}
