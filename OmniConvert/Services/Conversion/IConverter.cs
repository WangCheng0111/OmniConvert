using OmniConvert.Models;
using System.Threading;
using System.Threading.Tasks;

namespace OmniConvert.Services.Conversion;

public interface IConverter
{
    bool CanConvert(FormatCategory category, FormatDefinition target);

    string GetOutputExtension(FormatDefinition target) => target.Extension;

    Task ConvertAsync(string inputPath, string outputPath, FormatCategory category, FormatDefinition target, CancellationToken cancellationToken);
}
