# Failed initial stress oracle

This is preserved partial evidence, **not an accepted timing or completed stress sample**. The initial source fingerprint and exact compiler closures are recorded in the parent directory. The original watch log and analyzer observations are copied without alteration. The observer command is a textual JSON copy with a final CRLF.

The combined process acknowledged 100 measured Margin updates, two warmups, six undo/redo edits and Text, scope and compilation controls. Generation 113 then reported one diagnostic; its semantic store retained successful generation 112. The old oracle timed out waiting for an invalid member binding to remain reactive.

The `diagnose-invalid` and `diagnose-invalid-baseline` directories preserve independent small reproductions using the original combined and baseline compiler closures, respectively. Their observer adds a ten-second diagnostic timeout with actual label values. Both show the same result: the reactive state labels and valid direct LastName label update to revision 12; the invalid direct FirstName label remains at revision 11. State is 42 and the external subscriber count is one. These are failing reproductions, not completed performance runs.

The [recovery](../recovery/README.md) checks this existing invalid-expression fallback explicitly and requires full reactivity again after repair. It does not change cache eligibility or compiler/runtime behavior.
