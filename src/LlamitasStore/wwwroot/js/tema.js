// Se carga en <head> de forma síncrona para aplicar el tema ANTES de pintar (sin parpadeo).
// Tema por defecto: oscuro. La elección del usuario se recuerda en este navegador.
(function () {
    'use strict';
    var tema = 'dark';
    try {
        var guardado = localStorage.getItem('ls-tema');
        if (guardado === 'light' || guardado === 'dark') tema = guardado;
    } catch (e) { /* almacenamiento bloqueado: se usa el tema por defecto */ }
    var html = document.documentElement;
    html.setAttribute('data-bs-theme', tema);
    html.classList.add('js');
})();
