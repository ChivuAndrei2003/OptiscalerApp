using CommunityToolkit.Mvvm.ComponentModel;

namespace OptiscalerApp.ViewModels;

/// <summary>The Help page: every explanation in <see cref="HelpText" />, narrowed by a search box.</summary>
public sealed partial class HelpViewModel : ViewModelBase
{
    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private IReadOnlyList<HelpSection> _sections = HelpText.Sections;

    public bool HasNoMatches => Sections.Count == 0;

    partial void OnSearchTextChanged(string value)
    {
        var search = value.Trim();

        bool Matches(string text) { return text.Contains(search, StringComparison.OrdinalIgnoreCase); }

        // A section whose own title matches is shown whole; otherwise only its matching topics are.
        Sections = search.Length == 0
            ? HelpText.Sections
            : HelpText.Sections
                .Select(section => Matches(section.Title)
                            ? section
                            : section with
                            {
                                Topics = section.Topics.Where(t => Matches(t.Title) || Matches(t.Text)).ToList()
                            })
                .Where(section => section.Topics.Count > 0).ToList();
    }
}
