namespace Core;

/// Runs a game's lists of steps one step at a time from a stack of frames (innermost last), so that a step can
/// wait for a player (choose(...) or offer(...)) and carry on once they've answered. Frames are plain data, so
/// undo and bots copy them like the rest of the game. When nothing is left to run, the game's "turn" steps start
/// again (see StartTurn).
///
/// A step that starts more steps (trigger('deal'), a card's effect, try_move's rule) schedules them: they run, in
/// order, right after that step. Steps started from outside (an input) run at once.
public class StepRunner(Game game)
{
    const int MaxRepeats = 1000;
    const int MaxDepth = 50;

    public List<Frame> Frames { get; set; } = [];
    public bool Running { get; private set; }
    /// Set by choose(...) or offer(...) when its step has no answer yet: what to ask for.
    public WaitRequest? WaitRequest { get; private set; }
    /// A step is waiting for its answer (the innermost frame's): nothing runs until it gets one, or until steps are
    /// scheduled on top of it (an action that was never offered, done while an offer waits).
    public bool Waiting { get; set; }

    List<Frame> scheduled = [];
    /// The frame whose step is being worked out, and how many of its answers that step has used so far.
    Frame? stepping;
    int answersUsed;
    /// While above 0, steps are only scheduled (see Hold).
    int holding;
    bool waitedSinceTurn;
    int turnsRun;
    int idleTurns;
    bool turnsStopped;

    public bool IsWaiting => Frames.Count > 0 && !Running;

    /// How the turns are going, for the stalemate rule (see StartTurn). Snapshot copies it with the frames, so a bot
    /// trying a move out doesn't leave the turns it ran counted.
    public (int Run, int Idle, bool Waited, bool Stopped) Turns
    {
        get => (turnsRun, idleTurns, waitedSinceTurn, turnsStopped);
        set => (turnsRun, idleTurns, waitedSinceTurn, turnsStopped) = value;
    }

    /// Steps to run after the step being worked out, or straight away when started from outside (see Drive).
    public void Schedule(List<object?> steps, Card? card)
    {
        if (steps.Count == 0)
        {
            return;
        }
        int depth = stepping != null ? stepping.Depth + 1 : 0;
        if (depth >= MaxDepth)
        {
            game.ReportFailure($"Stopped: steps triggered each other {MaxDepth} levels deep (endless loop?)");
            return;
        }
        scheduled.Add(NewFrame(steps, card, depth, game.MoveContext()));
    }

    /// Runs frames until none are left or a step waits. A step that was waiting is worked out again on the way,
    /// so it waits again (or carries on, if it has its answer now).
    public void Drive()
    {
        if (Running || holding > 0 || Waiting && scheduled.Count == 0)
        {
            return;
        }
        Waiting = false;
        Running = true;
        RunFrames(0);
        Running = false;
    }

    /// Steps started until Release() are only scheduled, then run together in order: a drag's card effects and its
    /// action's steps, so an effect that waits for a choice holds up the rest.
    public void Hold()
    {
        holding++;
    }

    public void Release()
    {
        holding--;
        Drive();
    }

    /// Runs steps to the end at once, e.g. "deck_empty" in the middle of a draw. They can't wait for a choice.
    public void RunNow(List<object?> steps, Card? card)
    {
        int start = Frames.Count;
        bool outerRunning = Running;
        Frame? outerStepping = stepping;
        int outerAnswersUsed = answersUsed;
        List<Frame> outerScheduled = scheduled;
        MoveContext outerContext = game.MoveContext();
        Frames.Add(NewFrame(steps, card, stepping != null ? stepping.Depth + 1 : 0, outerContext));
        scheduled = [];
        Running = true;
        if (!RunFrames(start))
        {
            game.ReportFailure("Steps run in the middle of another step (like \"deck_empty\" during a draw) can't wait for a choice");
            EndScript(start);
        }
        Running = outerRunning;
        stepping = outerStepping;
        answersUsed = outerAnswersUsed;
        scheduled = outerScheduled;
        game.SetMoveContext(outerContext);
    }

    /// The player's choice for the waiting step: that step runs again, and choose(...) gives this answer.
    public void Answer(object? choice)
    {
        Accept(choice);
        Drive();
    }

    /// Gives the waiting step its answer without running it yet, e.g. an offer's action, whose steps run first.
    public void Accept(object? answer)
    {
        Frames[^1].Answers.Add(answer);
        Waiting = false;
    }

    /// Whether the step being worked out already has an answer for its next choose(...) or offer(...).
    public bool HasAnswer() => stepping != null && answersUsed < stepping.Answers.Count;

    /// choose(...) during a step: the answer already given, or null after asking for one (the step then stops).
    public object? AnswerOrWait(WaitRequest? request)
    {
        if (stepping == null)
        {
            game.ReportFailure("choose(...) only works in steps");
            return null;
        }
        if (answersUsed < stepping.Answers.Count)
        {
            answersUsed++;
            return stepping.Answers[answersUsed - 1];
        }
        WaitRequest = request;
        return null;
    }

    public bool HasLocal(string name) => FrameHolding(name) != -1;

    public object? Local(string name) => Frames[FrameHolding(name)].Locals[name];

    /// Sets a remembered name in the innermost list of the running trigger or rule that has it, or else in its
    /// outermost list, so the rest of that trigger or rule can read it.
    public void SetLocal(string name, object? value)
    {
        int holder = FrameHolding(name);
        Frames[holder != -1 ? holder : RootIndex()].Locals[name] = value;
    }

    /// The frames as copies whose remembered names and answers can change without touching these (for undo).
    public static List<Frame> CopyFrames(List<Frame> original) => original.Select(frame => frame.Copy()).ToList();

    static Frame NewFrame(List<object?> steps, Card? card, int depth, MoveContext context) =>
        new() { Steps = steps, Card = card, Depth = depth, Root = true, Context = context };

    /// Runs the frames above start until they're done (true) or a step waits (false).
    bool RunFrames(int start)
    {
        PushScheduled();
        while (Frames.Count > start || start == 0 && StartTurn())
        {
            RunStep(Frames[^1]);
            if (WaitRequest != null)
            {
                WaitRequest request = WaitRequest;
                WaitRequest = null;
                if (start > 0)
                {
                    return false;
                }
                Waiting = true;
                waitedSinceTurn = true;
                if (request is OfferRequest offer)
                {
                    game.Plays.OpenOffer(offer);
                }
                else
                {
                    game.Choice.OpenInline((ChoiceRequest)request);
                }
                return false;
            }
            PushScheduled();
        }
        return true;
    }

    /// Starts the "turn" steps again, if the game has them and nothing else is due (Game.NextTurnDue). When a whole
    /// round of turns goes by without anyone able to do anything (every offer had nothing possible), it's a
    /// stalemate, and the game is over.
    bool StartTurn()
    {
        if (turnsStopped || !game.NextTurnDue())
        {
            return false;
        }
        if (turnsRun > 0)
        {
            idleTurns = waitedSinceTurn ? 0 : idleTurns + 1;
        }
        waitedSinceTurn = false;
        if (idleTurns >= Math.Max(game.PlayersLeft().Count, 1))
        {
            game.LogLine("Nobody could do anything for a whole round of turns, so the game is over");
            game.EndGame([]);
            return false;
        }
        turnsRun++;
        game.LogLine("[turn] " + game.PlayerName(game.CurrentPlayer()));
        Frames.Add(new() { Steps = game.Rules.Turn, Root = true, Context = game.MoveContext(), Turn = true });
        return true;
    }

    /// Steps scheduled during the last step go on top, first one innermost-last, so they run in the order scheduled.
    void PushScheduled()
    {
        for (int i = scheduled.Count - 1; i >= 0; i--)
        {
            Frames.Add(scheduled[i]);
        }
        scheduled.Clear();
    }

    void RunStep(Frame frame)
    {
        if (game.Over && frame.Turn)
        {
            // The game ended (in this turn, or in an action done in it): the rest of the turn doesn't happen.
            EndScript(RootIndex(), failed: false);
            return;
        }
        game.SetMoveContext(frame.Context);
        if (frame.Kind == FrameKind.While && !frame.Checked)
        {
            CheckWhile(frame);
            return;
        }
        if (frame.Index >= frame.Steps.Count)
        {
            FinishList(frame);
            return;
        }
        object? step = frame.Steps[frame.Index];
        if (step is string blank && blank.Trim().Length == 0)
        {
            frame.Index++;
            return;
        }
        Dictionary<string, object?>? block = step as Dictionary<string, object?>;
        string text = step as string ?? ConditionOf(block!);
        if (block != null && block.ContainsKey("while"))
        {
            frame.Index++;
            Frames.Add(Body(frame, StepsOf(block, "do"), FrameKind.While, condition: text));
            return;
        }
        object? result = WorkOut(frame, text);
        if (WaitRequest != null)
        {
            return;
        }
        if (game.EvaluationFailed)
        {
            EndScript(RootIndex());
            return;
        }
        frame.Index++;
        if (frame.Answers.Count > 0)
        {
            frame.Answers = [];
        }
        if (block != null && block.ContainsKey("for"))
        {
            StartLoop(frame, block, result);
        }
        else if (block != null)
        {
            string branch = Values.IsTrue(result) ? "then" : "else";
            game.LogLine($"  if {text} → {branch}");
            if (StepsOf(block, branch).Count > 0)
            {
                Frames.Add(Body(frame, StepsOf(block, branch)));
            }
        }
    }

    /// Works out one step's text, with the answers to any choose(...) in it made so far.
    object? WorkOut(Frame frame, string text)
    {
        stepping = frame;
        answersUsed = 0;
        object? result = game.Evaluate(text, frame.Card);
        stepping = null;
        game.GameLog.FlushItems();
        return result;
    }

    /// The text an "if", a loop ("in") or a "while" works out.
    static string ConditionOf(Dictionary<string, object?> block) =>
        (block.GetValueOrDefault("if") ?? block.GetValueOrDefault("in") ?? block.GetValueOrDefault("while")) as string ?? "";

    static List<object?> StepsOf(Dictionary<string, object?> block, string key) =>
        block.GetValueOrDefault(key) as List<object?> ?? [];

    static Frame Body(Frame parent, List<object?> steps, FrameKind kind = FrameKind.List, string loopName = "", List<object?>? items = null, string condition = "") =>
        new()
        {
            Steps = steps, Card = parent.Card, Depth = parent.Depth, Context = parent.Context, Turn = parent.Turn,
            Kind = kind, LoopName = loopName, Items = items ?? [], Condition = condition,
        };

    void StartLoop(Frame frame, Dictionary<string, object?> step, object? items)
    {
        List<object?>? itemList = LoopItems(items);
        if (itemList == null)
        {
            game.ReportFailure($"Can't loop over \"{step["in"]}\": it should be a zone, a list, or a number");
            EndScript(RootIndex());
            return;
        }
        game.LogLine($"  for {step["for"]} in {step["in"]} ({itemList.Count})");
        if (itemList.Count == 0 || StepsOf(step, "do").Count == 0)
        {
            return;
        }
        Frame loop = Body(frame, StepsOf(step, "do"), FrameKind.Loop, (string)step["for"]!, itemList);
        loop.OuterPlayer = game.Variables.GetValueOrDefault(Game.PlayerVariable);
        Frames.Add(loop);
        SetLoopItem(loop);
    }

    /// Looping over "player" changes whose turn it is, so per-player names follow the loop.
    void SetLoopItem(Frame loop)
    {
        object? item = loop.Items[loop.Position];
        if (loop.LoopName == Game.PlayerVariable)
        {
            game.Variables[Game.PlayerVariable] = item;
        }
        else
        {
            loop.Locals[loop.LoopName] = item;
        }
        loop.Locals[loop.LoopName + Game.LoopNumberSuffix] = (long)loop.Position + 1;
    }

    void CheckWhile(Frame frame)
    {
        bool holds = Values.IsTrue(WorkOut(frame, frame.Condition));
        if (WaitRequest != null)
        {
            return;
        }
        if (game.EvaluationFailed)
        {
            EndScript(RootIndex());
        }
        else if (!holds)
        {
            game.LogLine($"  while {frame.Condition}: repeated {frame.Repeats} times");
            Frames.RemoveAt(Frames.Count - 1);
        }
        else if (frame.Repeats >= MaxRepeats)
        {
            game.ReportFailure($"Stopped: \"while {frame.Condition}\" was still true after {MaxRepeats} repeats");
            EndScript(RootIndex());
        }
        else
        {
            frame.Answers = [];
            frame.Repeats++;
            frame.Checked = true;
        }
    }

    /// The end of a list: a loop goes on to its next item, a while checks again, anything else is done.
    void FinishList(Frame frame)
    {
        if (frame.Kind == FrameKind.Loop && frame.Position + 1 < frame.Items.Count)
        {
            frame.Position++;
            frame.Index = 0;
            SetLoopItem(frame);
        }
        else if (frame.Kind == FrameKind.While)
        {
            frame.Index = 0;
            frame.Checked = false;
        }
        else
        {
            Pop();
        }
    }

    /// Stops the running trigger or rule (after a failure): its frames from index on are dropped. A failure in the
    /// turn stops the turns, rather than failing again every turn.
    void EndScript(int index, bool failed = true)
    {
        if (failed && index < Frames.Count && Frames[index].Turn)
        {
            game.ReportFailure("Turns stopped after that failure; fix the step and press Reload");
            turnsStopped = true;
        }
        while (Frames.Count > index)
        {
            Pop();
        }
    }

    void Pop()
    {
        Frame frame = Frames[^1];
        Frames.RemoveAt(Frames.Count - 1);
        if (frame.Kind == FrameKind.Loop && frame.LoopName == Game.PlayerVariable)
        {
            game.Variables[Game.PlayerVariable] = frame.OuterPlayer;
        }
    }

    /// The outermost frame of the trigger or rule that the innermost frame belongs to.
    int RootIndex()
    {
        for (int i = Frames.Count - 1; i >= 0; i--)
        {
            if (Frames[i].Root)
            {
                return i;
            }
        }
        return 0;
    }

    int FrameHolding(string name)
    {
        for (int i = Frames.Count - 1; i >= 0; i--)
        {
            if (Frames[i].Locals.ContainsKey(name))
            {
                return i;
            }
            if (Frames[i].Root)
            {
                return -1;
            }
        }
        return -1;
    }

    static List<object?>? LoopItems(object? items) => items switch
    {
        Zone zone => zone.Cards.Cast<object?>().ToList(),
        List<object?> list => [.. list],
        long or double => Enumerable.Range(1, Math.Max((int)Values.ToDouble(items), 0)).Select(number => (object?)(long)number).ToList(),
        _ => null,
    };
}
