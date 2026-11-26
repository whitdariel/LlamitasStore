// Catálogo y ficha de producto. Sin manejadores inline (la CSP los bloquea).
(function () {
    'use strict';

    // Enviar el formulario de orden al cambiar el <select>.
    document.querySelectorAll('form[data-autoenvio] select').forEach(function (select) {
        select.addEventListener('change', function () { select.form.submit(); });
    });

    // Galería: cambiar la imagen principal desde las miniaturas.
    var principal = document.getElementById('galeria-principal');
    if (principal) {
        document.querySelectorAll('[data-galeria-src]').forEach(function (boton) {
            boton.addEventListener('click', function () {
                principal.src = boton.getAttribute('data-galeria-src');
                principal.alt = boton.getAttribute('data-galeria-alt') || '';
                document.querySelectorAll('.ls-miniatura.activa').forEach(function (b) { b.classList.remove('activa'); });
                boton.classList.add('activa');
            });
        });
    }
})();
