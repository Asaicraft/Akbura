export function enableMobileTextInput(host) {
    if (!navigator.maxTouchPoints) {
        return;
    }

    let pendingInput = null;
    const reset = () => { pendingInput = null; };

    host.addEventListener('keydown', event => {
        pendingInput = event.isTrusted && event.key === 'Unidentified' &&
            event.target.classList?.contains('avalonia-input-element') ? event.target : null;
    }, true);

    host.addEventListener('keyup', reset, true);
    host.addEventListener('focusout', reset, true);
    host.addEventListener('compositionstart', reset, true);
    host.addEventListener('compositionend', reset, true);

    // Avalonia 12.0.4 receives keydown and composition events. Gboard can commit
    // a digit through input alone after sending an unidentified key.
    host.addEventListener('input', event => {
        const input = pendingInput;
        reset();
        if (input && event.target === input && event.isTrusted && event.inputType === 'insertText' &&
            !event.isComposing && event.data) {
            input.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true, data: event.data }));
        }
    }, true);
}
