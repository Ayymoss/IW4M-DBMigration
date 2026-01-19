namespace IWDataMigration.Abstractions;

/// <summary>
/// Handles console UI operations.
/// </summary>
public interface IConsoleService
{
    /// <summary>
    /// Displays the application header.
    /// </summary>
    void DisplayHeader();

    /// <summary>
    /// Displays migration instructions.
    /// </summary>
    /// <returns>True if user wants to continue, false otherwise.</returns>
    bool DisplayInstructions();

    /// <summary>
    /// Displays a section rule with the given title.
    /// </summary>
    void DisplayRule(string title, string color = "yellow");

    /// <summary>
    /// Displays a message to the console.
    /// </summary>
    void DisplayMessage(string message);

    /// <summary>
    /// Displays an error message.
    /// </summary>
    void DisplayError(string message);

    /// <summary>
    /// Displays the final success messages.
    /// </summary>
    void DisplayFinalMessages();

    /// <summary>
    /// Prompts the user to select from a list of choices.
    /// </summary>
    T Prompt<T>(string title, IEnumerable<T> choices) where T : notnull;

    /// <summary>
    /// Prompts the user for text input.
    /// </summary>
    string PromptText(string prompt, string? defaultValue = null);

    /// <summary>
    /// Prompts the user for confirmation.
    /// </summary>
    bool Confirm(string message);

    /// <summary>
    /// Waits for user to press any key.
    /// </summary>
    void WaitForKey(string? message = null);

    /// <summary>
    /// Displays an exception to the console.
    /// </summary>
    void DisplayException(Exception ex);
}
