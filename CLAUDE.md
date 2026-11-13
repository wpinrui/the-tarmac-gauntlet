# Rules for every thread

## Car modelling
- Never edit `docs/car-modelling.md` without Ivan's explicit permission in this thread. Propose the change in chat and wait for his yes.
- All 3D car modelling work follows `docs/car-modelling.md`. Read it in full before starting, and follow every step in order. No shortcuts or other methods.

## Questions are not instructions
- When Ivan asks a question, answer it and keep working. A question never means stop, undo or change course.
- Never stop, cancel or kill running work (a bake, a build, a render, a long script) unless Ivan's own words say to stop that work.

## GitHub Actions
- GitHub Actions are banned. Never add a workflow under `.github/`, and never wait on or rely on CI. Run checks locally before merging.

## Unity APIs
- Never call `GetInstanceID()`. Unity 6.6 treats it as a compile error and the build fails. For a dictionary key or a stable tie-break, use the object itself as the key, or `GetHashCode()`.
- Before merging any Unity C#, search the diff for APIs marked obsolete in Unity 6.6, and replace them.
