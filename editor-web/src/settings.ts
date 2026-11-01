export function installSettings(): void {
  const controls = document.querySelector<HTMLElement>('.font-controls')!;
  const dialog = document.createElement('dialog');
  dialog.setAttribute('aria-label', '설정');
  dialog.innerHTML = '<h2>설정</h2><p>글꼴과 크기는 모든 메모에 적용되며 다음 실행에도 유지됩니다.</p><div id="settings-controls"></div><div class="dialog-actions"><button type="button">닫기</button></div>';
  document.body.append(dialog);
  dialog.querySelector('button')!.addEventListener('click', () => dialog.close());
  dialog.querySelector('#settings-controls')!.append(controls);
  const open = () => {
    if (dialog.open) return;
    dialog.showModal();
  };
  window.addEventListener('devpostit-settings', open);
  window.addEventListener('keydown', event => { if (event.ctrlKey && event.key === ',') { event.preventDefault(); open(); } });
}
