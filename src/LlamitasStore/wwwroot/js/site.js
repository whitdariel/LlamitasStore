// Interacciones globales de LlamitasStore. Sin manejadores inline (la CSP los bloquea).
(function () {
    'use strict';

    // ---------- Tema claro / oscuro ----------
    var html = document.documentElement;
    document.querySelectorAll('[data-cambiar-tema]').forEach(function (boton) {
        var actualizarEtiqueta = function () {
            var oscuro = html.getAttribute('data-bs-theme') === 'dark';
            boton.setAttribute('aria-pressed', String(oscuro));
            boton.setAttribute('aria-label', oscuro ? 'Cambiar a tema claro' : 'Cambiar a tema oscuro');
        };
        actualizarEtiqueta();
        boton.addEventListener('click', function () {
            var nuevo = html.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
            html.setAttribute('data-bs-theme', nuevo);
            try { localStorage.setItem('ls-tema', nuevo); } catch (e) { /* sin almacenamiento: dura esta visita */ }
            actualizarEtiqueta();
        });
    });

    // ---------- Aparición suave al hacer scroll ----------
    var revelables = document.querySelectorAll('.ls-revelar');
    if ('IntersectionObserver' in window && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        var observador = new IntersectionObserver(function (entradas) {
            entradas.forEach(function (e) {
                if (e.isIntersecting) { e.target.classList.add('visible'); observador.unobserve(e.target); }
            });
        }, { rootMargin: '0px 0px -8% 0px' });
        revelables.forEach(function (el) { observador.observe(el); });
    } else {
        revelables.forEach(function (el) { el.classList.add('visible'); });
    }

    // ---------- Carriles de productos (botones anterior/siguiente) ----------
    document.querySelectorAll('[data-carril]').forEach(function (envoltura) {
        var carril = envoltura.querySelector('.ls-carril');
        if (!carril) return;
        envoltura.querySelectorAll('[data-carril-mover]').forEach(function (boton) {
            boton.addEventListener('click', function () {
                var dir = Number(boton.getAttribute('data-carril-mover'));
                carril.scrollBy({ left: dir * carril.clientWidth * 0.9, behavior: 'smooth' });
            });
        });
    });

    // ---------- Confirmación antes de acciones destructivas ----------
    document.querySelectorAll('form[data-confirmar]').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            if (!window.confirm(form.getAttribute('data-confirmar'))) e.preventDefault();
        });
    });

    // ---------- Favoritos sin recargar la página (si falla, el formulario se envía normal) ----------
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-favorito')) return;
        e.preventDefault();
        var boton = form.querySelector('button');
        if (boton) boton.disabled = true;

        fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'fetch', 'Accept': 'application/json' } })
            .then(function (r) {
                var tipo = r.headers.get('content-type') || '';
                if (!r.ok || r.redirected || tipo.indexOf('application/json') === -1) throw new Error('sin-json');
                return r.json();
            })
            .then(function (datos) {
                var id = form.getAttribute('data-favorito');
                // El mismo producto puede aparecer varias veces (carriles, relacionados): se actualizan todos.
                document.querySelectorAll('form[data-favorito="' + id + '"] button').forEach(function (b) {
                    b.classList.toggle('activo', datos.favorito);
                    b.setAttribute('aria-pressed', String(datos.favorito));
                    b.setAttribute('aria-label', b.getAttribute(datos.favorito ? 'data-texto-quitar' : 'data-texto-agregar'));
                });
                document.querySelectorAll('[data-contador-favoritos]').forEach(function (c) {
                    c.textContent = datos.total;
                    c.classList.toggle('d-none', datos.total === 0);
                });
                var estado = document.getElementById('busqueda-estado');
                if (estado) estado.textContent = datos.favorito ? 'Agregado a favoritos' : 'Quitado de favoritos';
            })
            .catch(function () { HTMLFormElement.prototype.submit.call(form); })
            .finally(function () { if (boton) boton.disabled = false; });
    });

    // ---------- Buscador con sugerencias (combobox accesible) ----------
    var entrada = document.getElementById('busqueda-global');
    var lista = document.getElementById('busqueda-sugerencias');
    var estado = document.getElementById('busqueda-estado');
    if (!entrada || !lista) return;

    var temporizador = null;
    var controlador = null;
    var opciones = [];
    var activa = -1;

    function cerrar() {
        lista.hidden = true;
        entrada.setAttribute('aria-expanded', 'false');
        entrada.removeAttribute('aria-activedescendant');
        activa = -1;
    }

    function marcar(indice) {
        opciones.forEach(function (op, i) { op.setAttribute('aria-selected', String(i === indice)); });
        activa = indice;
        if (indice >= 0) {
            entrada.setAttribute('aria-activedescendant', opciones[indice].id);
            opciones[indice].scrollIntoView({ block: 'nearest' });
        } else {
            entrada.removeAttribute('aria-activedescendant');
        }
    }

    function crear(etiqueta, clase, texto) {
        var el = document.createElement(etiqueta);
        if (clase) el.className = clase;
        if (texto !== undefined) el.textContent = texto; // textContent: nunca HTML de la respuesta (sin XSS)
        return el;
    }

    function opcion(id, href, hijos) {
        var li = crear('li', 'ls-sugerencia');
        li.id = id;
        li.setAttribute('role', 'option');
        li.setAttribute('aria-selected', 'false');
        var a = crear('a');
        a.href = href;
        a.tabIndex = -1;
        hijos.forEach(function (h) { a.appendChild(h); });
        li.appendChild(a);
        return li;
    }

    function titulo(texto) {
        var li = crear('li', 'ls-sugerencias-titulo', texto);
        li.setAttribute('role', 'presentation');
        return li;
    }

    function pintar(datos, q) {
        lista.replaceChildren();
        opciones = [];
        var n = 0;

        if (datos.categorias.length) {
            lista.appendChild(titulo('Categorías'));
            datos.categorias.forEach(function (c) {
                var li = opcion('sug-' + (n++), c.url, [crear('span', 'ls-sug-texto', c.nombre)]);
                lista.appendChild(li); opciones.push(li);
            });
        }
        if (datos.productos.length) {
            lista.appendChild(titulo('Productos'));
            datos.productos.forEach(function (p) {
                var img = crear('img');
                if (p.imagen) img.src = p.imagen;
                img.alt = '';
                var texto = crear('span', 'ls-sug-texto');
                texto.appendChild(crear('span', 'ls-sug-nombre', p.nombre));
                texto.appendChild(crear('span', 'ls-sug-meta', p.marca + ' · ' + p.categoria));
                var li = opcion('sug-' + (n++), p.url, [img, texto, crear('span', 'ls-sug-precio', p.precio)]);
                lista.appendChild(li); opciones.push(li);
            });
        }
        if (!opciones.length) {
            var vacio = crear('li', 'ls-sugerencias-vacio', 'Sin coincidencias para “' + q + '”. Presiona Enter para buscar en todo el catálogo.');
            vacio.setAttribute('role', 'presentation');
            lista.appendChild(vacio);
        }

        lista.hidden = false;
        entrada.setAttribute('aria-expanded', 'true');
        if (estado) estado.textContent = opciones.length ? opciones.length + ' sugerencias disponibles' : 'Sin sugerencias';
        marcar(-1);
    }

    entrada.addEventListener('input', function () {
        var q = entrada.value.trim();
        clearTimeout(temporizador);
        if (q.length < 2) { cerrar(); return; }
        temporizador = setTimeout(function () {
            if (controlador) controlador.abort();
            controlador = new AbortController();
            fetch('/catalogo/sugerencias?q=' + encodeURIComponent(q), { signal: controlador.signal, headers: { 'Accept': 'application/json' } })
                .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.json(); })
                .then(function (datos) { if (entrada.value.trim() === q) pintar(datos, q); })
                .catch(function (e) { if (e.name !== 'AbortError') cerrar(); });
        }, 220);
    });

    entrada.addEventListener('keydown', function (e) {
        if (lista.hidden) return;
        if (e.key === 'ArrowDown') { e.preventDefault(); if (opciones.length) marcar((activa + 1) % opciones.length); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); if (opciones.length) marcar(activa <= 0 ? opciones.length - 1 : activa - 1); }
        else if (e.key === 'Escape') { cerrar(); }
        else if (e.key === 'Enter' && activa >= 0) { e.preventDefault(); window.location.href = opciones[activa].querySelector('a').href; }
    });

    document.addEventListener('click', function (e) {
        if (!lista.contains(e.target) && e.target !== entrada) cerrar();
    });
})();
