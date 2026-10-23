using Core;

/// The player's log (designer info off): what happened, without the steps' workings. Lines are from the recordings.
public class GameLogTests
{
    [Theory]
    [InlineData("[turn] Player 1")]
    [InlineData("[action] Draw")]
    [InlineData("[Choose a color] chose Blue")]
    [InlineData("[game over] Game over")]
    [InlineData("[not allowed] 7♥: hand_1 → pile (requires Play a card: matches)")]
    [InlineData("Nobody could do anything for a whole round of turns, so the game is over")]
    [InlineData("  deck → hand_1: Red 3, Yellow 2")]
    [InlineData("  deck_2 → hand_2: 5 hidden cards")]
    [InlineData("  face up in tableau_1: 9♦")]
    [InlineData("  shuffle deck")]
    [InlineData("  Player 2 is out")]
    public void PlayersSeeWhatHappened(string line)
    {
        Assert.True(GameLog.ForPlayers(line));
    }

    [Theory]
    [InlineData("[trigger] start")]
    [InlineData("[bot 2]")]
    [InlineData("  active_color = Green")]
    [InlineData("  if count(deck) == 0 → else")]
    [InlineData("  for player in players (2)")]
    [InlineData("  while text(top(deck), 'on_pile') != '': repeated 0 times")]
    [InlineData("  Player 1 may: Play a card, Draw")]
    [InlineData("  choose one: Red, Blue, Green, Yellow")]
    [InlineData("  You: choose 2 (click the highlighted cards)")]
    [InlineData("  sort hand_1 by color, number")]
    [InlineData("  mark number = 4: a hidden card, a hidden card")]
    public void PlayersDontSeeHowTheStepsWorkedItOut(string line)
    {
        Assert.False(GameLog.ForPlayers(line));
    }
}
