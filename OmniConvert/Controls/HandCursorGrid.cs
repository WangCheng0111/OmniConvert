using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace OmniConvert.Controls;

public sealed class HandCursorGrid : Grid
{
    public HandCursorGrid()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}
