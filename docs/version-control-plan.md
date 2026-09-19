# Version control and traceability

Project repository: https://github.com/Gianluca-Ba/MSIT-5910-01-Capstone

Project author: Gianluca Barsaglini. Commit author and committer email: barsaglini.gianluca@gmail.com.

## Branching approach

`main` holds reviewed project snapshots. `development` holds integrated work before it is merged into main. Focused feature branches can isolate later implementation changes. Changes are reviewed together with documentation and relevant tests before integration. Merge commits record the integration of development milestones.

This is a single-author project. Two branches provide a structure for collaboration; they do not establish that other contributors participated. Any future contributor review will be documented through actual pull-request activity.

## Documentation and traceability

Source code, tests, documentation, and editable design files have separate folders. Commit messages describe concrete changes with prefixes such as docs, feat, fix, and test. Functional requirement identifiers will connect implementation work with specifications and tests.

Architecture and sequence diagram sources are versioned beside requirements. Design changes should update diagrams, specifications, and validation plans together. Git history preserves earlier decisions and makes revisions traceable.

Milestone tags and GitHub releases will identify later submission and implementation snapshots. The initial Unit 3 snapshot contains planning and design artifacts; executable services and software testing remain future work.

## Commit identity convention

Both author and committer use Gianluca Barsaglini and barsaglini.gianluca@gmail.com. Verify these fields before pushing. Commit messages describe project work and contain no automated-assistant attribution or co-author trailers.