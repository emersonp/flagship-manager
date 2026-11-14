using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Web.Forms;

// The fields a form posted, in order, as text: what a form is drawn from when it is shown again after a refusal.
public sealed class FormValues(IEnumerable<KeyValuePair<string, string>> fields)
{
    public static FormValues Empty { get; } = new([]);

    public IReadOnlyList<KeyValuePair<string, string>> Fields { get; } = fields.ToList();

    public static FormValues From(IFormCollection form)
    {
        return new FormValues(form.SelectMany(f => f.Value.Select(v => new KeyValuePair<string, string>(f.Key, v ?? ""))));
    }

    public bool Has(string name)
    {
        return Fields.Any(f => f.Key == name);
    }

    // The first value, or empty when the field was not posted.
    public string Get(string name)
    {
        return Fields.FirstOrDefault(f => f.Key == name).Value ?? "";
    }

    public IReadOnlyList<string> All(string name)
    {
        return Fields.Where(f => f.Key == name).Select(f => f.Value).ToList();
    }

    public FormValues With(string name, string value)
    {
        return new FormValues(Fields.Where(f => f.Key != name).Append(new KeyValuePair<string, string>(name, value)));
    }

    public FormValues With(IEnumerable<KeyValuePair<string, string>> more)
    {
        FormValues result = this;
        foreach (KeyValuePair<string, string> field in more)
        {
            result = result.With(field.Key, field.Value);
        }

        return result;
    }

    public FormValues Without(Func<string, bool> drop)
    {
        return new FormValues(Fields.Where(f => !drop(f.Key)));
    }

    // The id of an input made from its name: letters, digits, and underscores, so a name like offer[0].count is a usable id.
    public static string Slug(string name)
    {
        return "f-" + Regex.Replace(name, "[^A-Za-z0-9_]+", "-").Trim('-');
    }
}
