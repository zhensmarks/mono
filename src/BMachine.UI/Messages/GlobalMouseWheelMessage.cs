using Avalonia;

namespace BMachine.UI.Messages;

public record GlobalMouseWheelMessage(Point ScreenPosition, double DeltaX, double DeltaY);
