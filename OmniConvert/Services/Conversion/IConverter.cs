using OmniConvert.Models;
using System.Threading;
using System.Threading.Tasks;

namespace OmniConvert.Services.Conversion;

public interface IConverter
{
    bool CanConvert(FormatCategory category, FormatDefinition target);

    Task ConvertAsync(string inputPath, string outputPath, FormatCategory category, FormatDefinition target, CancellationToken cancellationToken);
}
