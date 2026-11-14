# Rules for every thread

## Car modelling
- Never edit `docs/car-modelling.md` without Ivan's explicit permission in this thread. Propose the change in chat and wait for his yes.
- All 3D car modelling work follows `docs/car-modelling.md`. Read it in full before starting, and follow every step in order. No shortcuts or other methods.

## Workflows
- When a task has a written workflow, that workflow is the only method. Read it in full and follow every step in order. The livery workflow is https://claude.ai/artifact/W5q7r67SdELNmkjpPhrQc1, and car modelling is `docs/car-modelling.md`.
- Never add steps, sources or references the workflow does not name. Never use an earlier brief, thread, PR or another team's work as a pattern.
- A brief gives only the task's facts and a link to the workflow. It adds no method of its own.
- A new rule from Ivan goes into the workflow itself the same turn, not only into briefs.

## Questions are not instructions
- When Ivan asks a question, answer it and keep working. A question never means stop, undo or change course.
- Never stop, cancel or kill running work (a bake, a build, a render, a long script) unless Ivan's own words say to stop that work.

## GitHub Actions
- GitHub Actions are banned. Never add a workflow under `.github/`, and never wait on or rely on CI. Run checks locally before merging.

## Unity APIs
- Never call `GetInstanceID()`. Unity 6.6 treats it as a compile error and the build fails. For a dictionary key or a stable tie-break, use the object itself as the key, or `GetHashCode()`.
- Before merging any Unity C#, search the diff for APIs marked obsolete in Unity 6.6, and replace them.

## Menu text
- Menu text never wraps. `Menus.uss` sets every Label to `white-space: nowrap` and `flex-shrink: 0`. Only text meant to run as a paragraph gets `white-space: normal` on its own class.
- Before merging menu UI, check every new label at its longest real value. It must sit on one line and must not be squeezed. Give it room, or clip it with an ellipsis.
- A label never has both its own text and child elements. Put the text in its own child label.

## Team leads
A thread titled `[TEAM LEAD] <area>` owns one area. It plans and directs. It never builds.

### Delegate
- Never write code, art, sound or assets yourself, however small. Every piece of work goes to a worker thread.
- Start worker threads only for work Ivan has asked for.
- Split work into big slices, one thread each. Run slices in parallel when they don't touch the same code.

### Before planning
- Read the area's code, briefs and earlier threads first. Never ask Ivan what another thread already settled.
- Tell Ivan what you found, and talk it through before writing any plan.
- Product, creative and naming calls are Ivan's: give options and a recommendation. Technical calls are yours: never ask him those.

### Briefs
- Each brief carries Ivan's rules for the area word for word, plus every decision so far.
- Anything visual or audible goes to Ivan in a viewer or listening page before it enters Unity. The preview must show what the game will show.
- When an established method exists, the worker implements it as written. Any deviation needs Ivan's OK.
- Save each brief under /mnt/project-files/<area>/.

### Track
- Every worker reports back to the lead when it merges or gets blocked. Ivan never chases a worker.
- New rules and corrections from Ivan go straight to the affected worker threads. The coordinator does not relay them.
- A bug Ivan reports goes back to the thread that built that part.
- Keep the area checklist current with PR numbers. Keep living lists in an artifact, not in chat.

### Report
- When Ivan should test, say whether it's work in progress or the full plan. For work in progress, say what's done, what isn't, and which failures to expect.
- Bring measured findings. Ivan decides what matters.
- Never ask Ivan to approve what he already approved in a worker thread.

### Title
- Keep the title `[TEAM LEAD] <area>`. Never rename it.
