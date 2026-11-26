// Panel administrativo. Sin manejadores inline (CSP).
(function () {
    'use strict';

    // Selects que se envían al cambiar (rol y estado de usuarios).
    document.querySelectorAll('form[data-autoenvio-admin] select').forEach(function (s) {
        s.addEventListener('change', function () { s.form.submit(); });
    });

    // Tooltip de los gráficos: al pasar el mouse o enfocar con teclado una barra.
    document.querySelectorAll('[data-grafico]').forEach(function (grafico) {
        var tooltip = grafico.querySelector('.ls-tooltip');
        if (!tooltip) return;
        function mostrar(marca) {
            tooltip.textContent = marca.getAttribute('data-tooltip');
            tooltip.hidden = false;
            var caja = grafico.getBoundingClientRect();
            var r = marca.getBoundingClientRect();
            var x = r.left - caja.left + r.width / 2;
            tooltip.style.left = Math.min(Math.max(x, 80), caja.width - 80) + 'px';
            tooltip.style.top = (r.top - caja.top) + 'px';
        }
        function ocultar() { tooltip.hidden = true; }
        grafico.querySelectorAll('[data-tooltip]').forEach(function (m) {
            m.addEventListener('mouseenter', function () { mostrar(m); });
            m.addEventListener('focus', function () { mostrar(m); });
            m.addEventListener('mouseleave', ocultar);
            m.addEventListener('blur', ocultar);
        });
    });
})();
