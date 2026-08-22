// Working on a .cmp from inside the editor: making one, saying what it builds, and clearing up
// after a build.
//
// **These are the only place in this extension that writes Compass's own file formats**, which is
// worth being uneasy about. Everything else here asks the compiler and reports what it said —
// which project claims a file, what a file declares, whether a program checks — precisely so that
// no second reader of a format exists to drift from the first.
//
// What makes it tolerable is that none of this reads a `.cmp`. Adding a source is putting a line
// in before `end project`; removing one is taking a line out that names what was asked about;
// setting the entry point is replacing a line that opens with the word. No structure is inferred
// and nothing is rewritten wholesale, so a format that gains a word gains it here for free.
//
// And the language server validates the file as it is edited, so an edit that lands wrong is in
// the Problems panel before the reader has looked away. Anything needing more understanding than
// this belongs in `cm` rather than here.

const path = require('path');

/**
 * A line to put into a file, ending the way that file's other lines end.
 *
 * **Splitting on `\n` leaves the `\r` of a CRLF file on the end of every line**, which is what
 * keeps the untouched ones exactly as they were. A line inserted without one is then the only
 * LF line in the file — mixed endings written into somebody's project by a command they ran to
 * add a file, and shown as a whole-file change by whatever they use to review it.
 */
function lineIn(text, written) {
    return text.includes('\r\n') ? `${written}\r` : written;
}

/**
 * The line a project ends with, which is what every insertion goes in front of.
 *
 * Found from the bottom, so a project whose text is malformed above still takes an edit in the
 * one place that is certainly the end of it.
 */
function closingLine(lines) {
    for (let at = lines.length - 1; at >= 0; at--) {
        if (lines[at].trim().startsWith('end project')) {
            return at;
        }
    }

    return -1;
}

/**
 * How the lines of this project are indented, taken from what is already there.
 *
 * Read rather than assumed, because a file somebody hand-wrote is the one being edited and an
 * insertion in a different style is the first thing they will notice.
 */
function indentOf(lines) {
    for (const line of lines) {
        const written = /^(\s+)(source|reference|entry|output|ignore)\b/.exec(line);

        if (written) {
            return written[1];
        }
    }

    return '    ';
}

/**
 * The text of a project with one more `source` in it, or null where it already lists that path.
 *
 * Written with forward slashes whatever the platform, matching how a `.cmp` names paths — a file
 * listed with backslashes reads on one machine and not on the next, which is the kind of thing
 * that is found by somebody else, later, on a different computer.
 */
function withSource(text, projectPath, filePath) {
    const relative = relativeTo(projectPath, filePath);
    const lines = text.split('\n');
    const closing = closingLine(lines);

    if (closing < 0) {
        return null;
    }

    if (lines.some(line => sourceNamed(line) === relative)) {
        return null;
    }

    // After the last source rather than at the top, so the order a reader put them in is kept and
    // a `reference` stays above what it is referenced by.
    let at = closing;

    for (let line = 0; line < closing; line++) {
        if (sourceNamed(lines[line]) !== null) {
            at = line + 1;
        }
    }

    lines.splice(at, 0, lineIn(text, `${indentOf(lines)}source ${relative}`));

    return lines.join('\n');
}

/** The text of a project with a `source` taken out, or null where it lists no such path. */
function withoutSource(text, projectPath, filePath) {
    const relative = relativeTo(projectPath, filePath);
    const lines = text.split('\n');
    const at = lines.findIndex(line => sourceNamed(line) === relative);

    if (at < 0) {
        return null;
    }

    lines.splice(at, 1);

    return lines.join('\n');
}

/**
 * The text of a project that starts at a given type, replacing whatever it started at before.
 *
 * Put where the old one was when there is one, and above the sources otherwise — which is the
 * order the samples are written in, and reads as a heading rather than an afterthought.
 */
function withEntry(text, type) {
    const lines = text.split('\n');
    const written = lineIn(text, `${indentOf(lines)}entry ${type}`);
    const at = lines.findIndex(line => /^\s*entry\b/.test(line));

    if (at >= 0) {
        lines[at] = written;
        return lines.join('\n');
    }

    const closing = closingLine(lines);

    if (closing < 0) {
        return null;
    }

    const first = lines.findIndex(line => sourceNamed(line) !== null);

    lines.splice(first < 0 ? closing : first, 0, written);

    return lines.join('\n');
}

/**
 * The text of a project written into a given folder, replacing wherever it was written before.
 *
 * Below the sources rather than above them, which is the opposite of where `entry` goes and is
 * the order the samples are written in: what a build is made of comes first, and where it lands
 * comes last. Replacing rather than adding, because a project is written to one place — a second
 * `output` is an error, so writing one would break the file this is editing.
 */
function withOutput(text, folder) {
    const lines = text.split('\n');
    const written = lineIn(text, `${indentOf(lines)}output ${folder}`);
    const at = lines.findIndex(line => /^\s*output\b/.test(line));

    if (at >= 0) {
        lines[at] = written;
        return lines.join('\n');
    }

    const closing = closingLine(lines);

    if (closing < 0) {
        return null;
    }

    lines.splice(closing, 0, written);

    return lines.join('\n');
}

/**
 * What a line lists as a source, or null for a line that lists none.
 *
 * Deliberately not a reading of the format — it recognises one word at the start of a line and
 * takes the rest. A line this does not recognise is left exactly as it was.
 */
function sourceNamed(line) {
    const written = /^\s*source\s+(\S.*?)\s*$/.exec(line);

    return written ? written[1] : null;
}

/** A path as a `.cmp` names it: relative to the project, with forward slashes. */
function relativeTo(projectPath, filePath) {
    return path.relative(path.dirname(projectPath), filePath).split(path.sep).join('/');
}

/** Whether a file sits inside the folder holding a project, which is as far as one may list. */
function within(projectPath, filePath) {
    const relative = relativeTo(projectPath, filePath);

    return relative.length > 0 && !relative.startsWith('../') && !path.isAbsolute(relative);
}

module.exports = {
    closingLine,
    indentOf,
    relativeTo,
    sourceNamed,
    withEntry,
    withOutput,
    withSource,
    withoutSource,
    within,
};
