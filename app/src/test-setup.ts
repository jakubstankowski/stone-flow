/**
 * jsdom nie implementuje modalnego `<dialog>`. Minimalny zamiennik ustawia tylko
 * atrybut `open`; pułapkę fokusu sprawdza się w przeglądarce.
 */
if (typeof HTMLDialogElement !== 'undefined' && !HTMLDialogElement.prototype.showModal) {
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) {
    this.setAttribute('open', '');
  };
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) {
    this.removeAttribute('open');
    this.dispatchEvent(new Event('close'));
  };
}
