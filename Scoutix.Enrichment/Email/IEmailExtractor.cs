namespace Scoutix.Enrichment.Email;

public interface IEmailExtractor
{
    List<string> Extract(string html);
    string? PickBest(List<string> candidates);
}
