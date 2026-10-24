using System.Collections.Generic;

namespace Biubiu
{
    public static class CardOrder
    {
        public static List<ProjectCard> Stabilize(IList<long> previous, IList<ProjectCard> incoming)
        {
            List<ProjectCard> ordered = new List<ProjectCard>();
            Dictionary<long, ProjectCard> byHwnd = new Dictionary<long, ProjectCard>();
            if (incoming != null)
            {
                for (int i = 0; i < incoming.Count; i++)
                {
                    ProjectCard card = incoming[i];
                    if (card == null || byHwnd.ContainsKey(card.Hwnd))
                        continue;
                    byHwnd.Add(card.Hwnd, card);
                }
            }

            Dictionary<long, bool> used = new Dictionary<long, bool>();
            if (previous != null)
            {
                for (int i = 0; i < previous.Count; i++)
                {
                    ProjectCard card;
                    if (!byHwnd.TryGetValue(previous[i], out card))
                        continue;
                    ordered.Add(card);
                    used[card.Hwnd] = true;
                }
            }

            if (incoming != null)
            {
                for (int i = 0; i < incoming.Count; i++)
                {
                    ProjectCard card = incoming[i];
                    if (card == null || used.ContainsKey(card.Hwnd))
                        continue;
                    ordered.Add(card);
                    used[card.Hwnd] = true;
                }
            }
            return ordered;
        }
    }
}
