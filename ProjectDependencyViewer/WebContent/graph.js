// Catch uncaught errors and forward to C# via WebView2 message bridge
window.onerror = function (msg, url, line, col, error) {
  var detail = msg + ' at ' + url + ':' + line + ':' + col;
  if (error && error.stack) detail += '\n' + error.stack;
  try { window.chrome.webview.postMessage('JS_ERROR: ' + detail); } catch (_) {}
};

window.addEventListener('unhandledrejection', function (e) {
  var detail = 'Unhandled rejection: ';
  if (e.reason) detail += (e.reason.message || e.reason) + (e.reason.stack ? '\n' + e.reason.stack : '');
  try { window.chrome.webview.postMessage('JS_ERROR: ' + detail); } catch (_) {}
});

try {
  cytoscape.use(cytoscapeElk);
} catch (e) {
  try { window.chrome.webview.postMessage('JS_ERROR: cytoscape.use(cytoscapeElk) failed: ' + e.message); } catch (_) {}
}
try { window.chrome.webview.postMessage('JS_INIT: graph.js starting'); } catch (_) {}

document.getElementById('legend-toggle').addEventListener('click', function () {
  const panel = document.getElementById('legend-panel');
  const open = panel.classList.toggle('open');
  this.textContent = open ? '×' : 'ℹ';
  this.title = open ? 'Hide legend' : 'Toggle legend';
});

const typeColors = {
  'Project': '#27AE60',
  'NuGet':   '#2E75B6',
  'Dll':     '#E67E22'
};

const typeLabels = {
  'Project': 'Project Reference',
  'NuGet':   'NuGet Package',
  'Dll':     'DLL Reference'
};

function shadeColor(hex, amount) {
  const num = parseInt(hex.replace('#', ''), 16);
  const r = Math.min(255, Math.max(0, (num >> 16) + amount));
  const g = Math.min(255, Math.max(0, ((num >> 8) & 0xFF) + amount));
  const b = Math.min(255, Math.max(0, (num & 0xFF) + amount));
  return '#' + ((1 << 24) + (r << 16) + (g << 8) + b).toString(16).slice(1);
}

const cy = cytoscape({
  container: document.getElementById('cy'),
  style: [
    {
      selector: 'node',
      style: {
        'shape': 'roundrectangle',
        'width': 'label',
        'height': 'label',
        'padding': '10px',
        'background-color': 'data(color)',
        'background-opacity': 0.92,
        'border-width': 2,
        'border-color': 'data(borderColor)',
        'label': 'data(label)',
        'text-wrap': 'wrap',
        'text-valign': 'center',
        'text-halign': 'center',
        'color': '#fff',
        'font-size': '11px',
        'font-family': "'Segoe UI', sans-serif",
        'text-outline-width': 0
      }
    },
    {
      selector: 'edge',
      style: {
        'width': 1.8,
        'line-color': 'data(edgeColor)',
        'target-arrow-color': 'data(edgeColor)',
        'target-arrow-shape': 'triangle',
        'curve-style': 'bezier',
        'arrow-scale': 1.1,
        'opacity': 0.7
      }
    },
    {
      selector: 'node:selected',
      style: {
        'border-width': 3,
        'border-color': '#FFD700',
        'background-opacity': 1
      }
    },
    {
      selector: 'node.highlighted',
      style: {
        'border-width': 3,
        'border-color': '#FFD700'
      }
    },
    {
      selector: 'edge.highlighted',
      style: {
        'width': 3,
        'opacity': 1,
        'z-index': 10
      }
    },
    {
      selector: 'node.dimmed',
      style: {
        'opacity': 0.25
      }
    },
    {
      selector: 'edge.dimmed',
      style: {
        'opacity': 0.1
      }
    }
  ],
  elements: [],
  layout: { name: 'grid' },
  wheelSensitivity: 0.3,
  minZoom: 0.1,
  maxZoom: 5
});

try { window.chrome.webview.postMessage('JS_INIT: cytoscape instance created, container=' + (cy.container() ? 'ok' : 'null')); } catch (_) {}

const tooltip = document.getElementById('tooltip');

cy.on('mouseover', 'node', function (e) {
  const n = e.target.data();
  const lines = [n.name];
  lines.push('Type: ' + (typeLabels[n.nodeType] || n.nodeType));
  if (n.version) lines.push('Version: ' + n.version);
  tooltip.textContent = lines.join('\n');
  tooltip.style.display = 'block';
});

cy.on('mousemove', 'node', function (e) {
  const pos = e.renderedPosition || e.cyRenderedPosition;
  tooltip.style.left = (pos.x + 14) + 'px';
  tooltip.style.top = (pos.y + 14) + 'px';
});

cy.on('mouseout', 'node', function () {
  tooltip.style.display = 'none';
});

let highlightDepth = 1;

function setHighlightDepth(depth) {
  highlightDepth = Math.max(1, Math.min(depth, 10));
}

function highlightNeighborhood(startNodes) {
  cy.elements().removeClass('highlighted dimmed');
  if (startNodes.empty()) return;
  let collected = startNodes.closedNeighborhood();
  for (let i = 1; i < highlightDepth; i++) {
    let expanded = cy.collection();
    collected.nodes().forEach(function (n) {
      expanded = expanded.merge(n.closedNeighborhood());
    });
    collected = collected.merge(expanded);
  }
  cy.elements().not(collected).addClass('dimmed');
  collected.addClass('highlighted');
}

cy.on('tap', 'node', function (e) {
  highlightNeighborhood(e.target);
});

cy.on('tap', function (e) {
  if (e.target === cy) {
    cy.elements().removeClass('highlighted dimmed');
  }
});

var LARGE_GRAPH_THRESHOLD = 800;

const elkLayoutOptions = {
  name: 'elk',
  elk: {
    algorithm: 'layered',
    'elk.direction': 'DOWN',
    'elk.layered.spacing.nodeNodeBetweenLayers': '50',
    'elk.layered.spacing.nodeNode': '25',
    'elk.spacing.componentComponent': '40',
    'elk.layered.nodePlacement.strategy': 'BRANDES_KOEPF',
    'elk.layered.crossingMinimization.strategy': 'LAYER_SWEEP',
    'elk.layered.wrapping.strategy': 'MULTI_EDGE',
    'elk.layered.compaction.connectedComponents': 'true',
    'elk.layered.compaction.postCompaction.strategy': 'EDGE_LENGTH',
    'elk.layered.considerModelOrder.strategy': 'NODES_AND_EDGES'
  }
};

const coseLayoutOptions = {
  name: 'cose',
  nodeDimensionsIncludeLabels: true,
  idealEdgeLength: function () { return 40; },
  nodeRepulsion: function () { return 2000; },
  edgeElasticity: function () { return 150; },
  gravity: 0.8,
  numIter: 1000,
  randomize: true,
  nestingFactor: 1.2,
  nodeOverlap: 20
};

let currentLayout = null;

function stopCurrentLayout() {
  if (currentLayout) {
    currentLayout.stop();
    currentLayout = null;
  }
}

function runGridFallback(fit) {
  try {
    currentLayout = cy.layout({ name: 'grid', fit: fit, padding: 30, stop: function () { currentLayout = null; } });
    currentLayout.run();
  } catch (_) {}
}

function runFullLayout(fit) {
  stopCurrentLayout();
  var nodeCount = cy.nodes().length;
  var edgeCount = cy.edges().length;
  var useElk = nodeCount <= LARGE_GRAPH_THRESHOLD;
  var layoutName = useElk ? 'elk' : 'cose';
  try { window.chrome.webview.postMessage('JS_LAYOUT: starting ' + layoutName + ' layout (' + nodeCount + ' nodes, ' + edgeCount + ' edges)'); } catch (_) {}

  var baseOptions = useElk ? elkLayoutOptions : coseLayoutOptions;
  try {
    var layoutOptions = Object.assign({}, baseOptions, {
      animate: false,
      fit: fit,
      padding: 30,
      stop: function () {
        currentLayout = null;
        try { window.chrome.webview.postMessage('JS_LAYOUT: ' + layoutName + ' layout completed'); } catch (_) {}
      }
    });
    currentLayout = cy.layout(layoutOptions);
    var runResult = currentLayout.run();
    // ELK layout returns a promise; catch async failures and fall back to grid
    if (runResult && typeof runResult.then === 'function') {
      runResult.then(null, function (err) {
        try { window.chrome.webview.postMessage('JS_ERROR: ' + layoutName + ' async layout failed: ' + (err && err.message || err) + ', falling back to grid'); } catch (_) {}
        currentLayout = null;
        runGridFallback(fit);
      });
    }
  } catch (e) {
    try { window.chrome.webview.postMessage('JS_ERROR: ' + layoutName + ' layout failed: ' + e.message + ', falling back to grid'); } catch (_) {}
    currentLayout = null;
    runGridFallback(fit);
  }
}

function buildNodeData(n) {
  const color = typeColors[n.nodeType] || '#95A5A6';
  const borderColor = shadeColor(color, -30);
  let label = n.name;
  if (n.version) {
    label += '\n' + n.version;
  }
  return {
    id: n.id,
    label: label,
    name: n.name,
    nodeType: n.nodeType,
    version: n.version,
    color: color,
    borderColor: borderColor
  };
}

function renderGraph(jsonStr) {
  // Ensure cytoscape has up-to-date container dimensions (handles Collapsed→Visible transition)
  cy.resize();
  var container = cy.container();
  try { window.chrome.webview.postMessage('JS_RENDER: container ' + (container ? container.offsetWidth + 'x' + container.offsetHeight : 'null')); } catch (_) {}

  const data = JSON.parse(jsonStr);

  if (!data.nodes || data.nodes.length === 0) {
    stopCurrentLayout();
    cy.elements().remove();
    updateLegend();
    return;
  }

  const hadElements = cy.nodes().length > 0;

  // Build lookup maps for new data
  const newNodeMap = {};
  data.nodes.forEach(function (n) { newNodeMap[n.id] = n; });
  const newEdgeMap = {};
  data.edges.forEach(function (e) { newEdgeMap[e.source + '->' + e.target] = e; });

  // Remove nodes no longer present (also removes their connected edges)
  cy.nodes().filter(function (node) { return !newNodeMap[node.id()]; }).remove();
  // Remove remaining edges no longer present
  cy.edges().filter(function (edge) { return !newEdgeMap[edge.id()]; }).remove();

  // Add or update nodes, tracking which ones are new
  const addedNodeIds = [];
  data.nodes.forEach(function (n) {
    const nodeData = buildNodeData(n);
    const existing = cy.getElementById(n.id);
    if (existing.nonempty()) {
      existing.data(nodeData);
    } else {
      cy.add({ group: 'nodes', data: nodeData });
      addedNodeIds.push(n.id);
    }
  });

  // Add new edges
  data.edges.forEach(function (e) {
    const edgeId = e.source + '->' + e.target;
    if (cy.getElementById(edgeId).empty()) {
      const edgeColor = typeColors[e.type] || '#B0BEC5';
      cy.add({
        group: 'edges',
        data: { id: edgeId, source: e.source, target: e.target, edgeColor: edgeColor, type: e.type }
      });
    }
  });

  // Determine layout strategy
  if (!hadElements || addedNodeIds.length === data.nodes.length) {
    // First render or entirely new graph: full layout with fit
    runFullLayout(true);
  } else if (addedNodeIds.length > 0) {
    // Filter expanded to include new nodes: re-layout
    runFullLayout(true);
  }
  // else: only removals or data updates — positions and viewport are already preserved

  updateLegend();
}

function updateLegend() {
  const types = {};
  cy.nodes().forEach(function (n) {
    const t = n.data('nodeType');
    if (t) types[t] = true;
  });

  const container = document.getElementById('legend-items');
  const keys = Object.keys(types);
  if (keys.length === 0) {
    container.innerHTML = '<span class="legend-empty">No data</span>';
    return;
  }

  const order = ['Project', 'NuGet', 'Dll'];
  let html = '';
  order.forEach(function (t) {
    if (types[t]) {
      const color = typeColors[t];
      html += '<div class="legend-item">' +
        '<span class="legend-swatch" style="background:' + color + '"></span>' +
        typeLabels[t] + '</div>';
    }
  });
  container.innerHTML = html;
}

function selectNodeById(id) {
  cy.elements().removeClass('highlighted dimmed');
  if (!id) return;
  const node = cy.getElementById(id);
  if (node.empty()) return;
  highlightNeighborhood(node);
  cy.animate({ center: { eles: node }, duration: 300 });
}

function resetView() {
  cy.resize();
  if (cy.nodes().length > 0) {
    cy.fit(cy.elements(), 30);
  }
}

function resetLayout() {
  cy.resize();
  if (cy.nodes().length > 0) {
    runFullLayout(true);
  }
}
