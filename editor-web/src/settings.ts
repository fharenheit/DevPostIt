export function installSettings(): void {
  const dialog = document.querySelector<HTMLDialogElement>('#settings-dialog')!;
  dialog.querySelector('#settings-close')!.addEventListener('click', () => dialog.close());
  const open = () => {
    if (dialog.open) return;
    dialog.showModal();
  };
  window.addEventListener('devpostit-settings', open);
  window.addEventListener('keydown', event => { if (event.ctrlKey && event.key === ',') { event.preventDefault(); open(); } });
}
