namespace BingLan.Core.Dock;

/// <summary>
/// Places desktop cards when an arrangement of monitors appears that has no remembered
/// spots for them: a card already fully on a work area stays, the others are gathered
/// into the nearest work area and cascaded so the arrivals do not stack on one another
/// or on the cards that stayed. When the work areas are too crowded for a card at its
/// current size, it is offered smaller sizes — stepped down to a floor the caller
/// passes per card — before falling back to the nearest edge at full size.
/// </summary>
public static class DisplayArrivalRules
{
    /// <summary>Distance between cascade slots, in pixels.</summary>
    public const int CascadeStride = 24;

    /// <summary>Step between the sizes an overcrowded card is offered, as a share of its own size.</summary>
    public const double ShrinkStep = 0.1;

    /// <summary>Smallest share of its own size a card is offered without an explicit floor.</summary>
    public const double MinimumScale = 0.5;

    public static IReadOnlyList<PixelRect> Gather(
        IReadOnlyList<PixelRect> cards,
        IReadOnlyList<PixelRect> workAreas,
        PixelRect fallback,
        IReadOnlyList<PixelRect>? reserved = null,
        IReadOnlyList<(int Width, int Height)?>? minimumSizes = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(workAreas);
        if (minimumSizes is not null && minimumSizes.Count != cards.Count)
        {
            throw new ArgumentException(
                "Minimum sizes, when given, must have one entry per card.",
                nameof(minimumSizes));
        }

        var validAreas = workAreas.Where(area => area.HasArea).ToList();
        if (validAreas.Count == 0)
        {
            if (!fallback.HasArea)
            {
                throw new ArgumentException(
                    "At least one work area or a valid fallback must have a positive area.",
                    nameof(workAreas));
            }
            validAreas.Add(fallback);
        }

        // Spots already taken by cards that are not part of this gather, such as ones
        // restored from their remembered position; arrivals must not stack on them.
        var occupied = reserved?.Where(spot => spot.HasArea).ToList() ?? [];
        var targets = new PixelRect[cards.Count];

        // Cards already fully on a work area keep their spot and claim it first.
        var arrivals = new List<int>();
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            if (!card.HasArea)
            {
                throw new ArgumentException(
                    $"Card bounds must have a positive area (index {index}).",
                    nameof(cards));
            }
            if (validAreas.Any(area => FitsInside(card, area)))
            {
                targets[index] = card;
                occupied.Add(card);
            }
            else
            {
                arrivals.Add(index);
            }
        }

        // Cascading follows the reading order of where the cards sat, so the layout of
        // the removed screen is echoed by the order of the arrivals.
        arrivals.Sort((first, second) =>
        {
            var byTop = cards[first].Top.CompareTo(cards[second].Top);
            return byTop != 0 ? byTop : cards[first].Left.CompareTo(cards[second].Left);
        });

        foreach (var index in arrivals)
        {
            var card = cards[index];
            var area = NearestArea(card, validAreas);
            var floor = minimumSizes?[index];
            PixelRect? placed = null;
            foreach (var size in CandidateSizes(card, floor))
            {
                placed = Cascade(Anchor(card, area, size.Width, size.Height), area, occupied);
                if (placed is not null)
                {
                    break;
                }
            }
            // No size fits anywhere: the card stays at full size on the nearest edge,
            // visible even where it overlaps.
            targets[index] = placed ?? PlacementResolver.ClampInside(card, area);
            occupied.Add(targets[index]);
        }

        return targets;
    }

    // The sizes an overcrowded card is offered: its own size first, then the stepped
    // shares, each held at the caller's floor; without a floor only the full size.
    private static IEnumerable<(int Width, int Height)> CandidateSizes(
        PixelRect card,
        (int Width, int Height)? floor)
    {
        if (floor is null)
        {
            yield return (card.Width, card.Height);
            yield break;
        }

        (int, int) last = default;
        for (var scale = 1.0; scale >= MinimumScale - 0.001; scale -= ShrinkStep)
        {
            var width = Math.Max((int)Math.Round(card.Width * scale), floor.Value.Width);
            var height = Math.Max((int)Math.Round(card.Height * scale), floor.Value.Height);
            if ((width, height) != last)
            {
                last = (width, height);
                yield return (width, height);
            }
        }
    }

    // The card's spot for the given size, clamped fully into the area.
    private static PixelRect Anchor(PixelRect card, PixelRect area, int width, int height)
    {
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);
        var left = Math.Clamp(card.Left, area.Left, area.Right - width);
        var top = Math.Clamp(card.Top, area.Top, area.Bottom - height);
        return new PixelRect(left, top, left + width, top + height);
    }

    // Walks the stride grid of the work area from the slot nearest the card's clamped
    // spot, wrapping once, and returns the first slot clear of the cards already
    // placed; a grid without a single free slot returns null.
    private static PixelRect? Cascade(PixelRect anchor, PixelRect area, List<PixelRect> occupied)
    {
        var columns = Math.Max(1, (area.Width - anchor.Width) / CascadeStride + 1);
        var rows = Math.Max(1, (area.Height - anchor.Height) / CascadeStride + 1);
        var slotCount = columns * rows;
        var startColumn = Math.Clamp((anchor.Left - area.Left) / CascadeStride, 0, columns - 1);
        var startRow = Math.Clamp((anchor.Top - area.Top) / CascadeStride, 0, rows - 1);
        var start = (startRow * columns) + startColumn;

        for (var step = 0; step < slotCount; step++)
        {
            var slot = (start + step) % slotCount;
            var left = area.Left + ((slot % columns) * CascadeStride);
            var top = area.Top + ((slot / columns) * CascadeStride);
            var candidate = new PixelRect(left, top, left + anchor.Width, top + anchor.Height);
            if (occupied.All(taken => !candidate.Intersects(taken)))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool FitsInside(PixelRect card, PixelRect area) =>
        card.Left >= area.Left && card.Right <= area.Right
        && card.Top >= area.Top && card.Bottom <= area.Bottom;

    private static PixelRect NearestArea(PixelRect card, List<PixelRect> areas) =>
        areas
            .Select((area, index) => new
            {
                Area = area,
                Index = index,
                Distance = PlacementResolver.DistanceSquared(card, area)
            })
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Index)
            .First()
            .Area;
}
