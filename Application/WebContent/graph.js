cytoscape.use(cytoscapeElk);

document.getElementById('legend-toggle').addEventListener('click', function () {
  const panel = document.getElementById('legend-panel');
  const open = panel.classList.toggle('open');
  this.textContent = open ? '×' : 'ℹ';
  this.title = open ? 'Hide legend' : 'Toggle legend';
});

const levelColors = [
  '#2E75B6', '#27AE60', '#E67E22', '#8E44AD', '#E74C3C',
  '#16A085', '#D35400', '#2980B9', '#C0392B', '#7D3C98'
];
const unknownColor = '#95A5A6';

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
        'line-color': '#B0BEC5',
        'target-arrow-color': '#78909C',
        'target-arrow-shape': 'triangle',
        'curve-style': 'bezier',
        'arrow-scale': 1.1,
        'opacity': 0.8
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
        'line-color': '#E74C3C',
        'target-arrow-color': '#E74C3C',
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

const tooltip = document.getElementById('tooltip');

cy.on('mouseover', 'node', function (e) {
  const n = e.target.data();
  const lines = [n.id];
  lines.push('SN: ' + n.sn + '   Level: ' + n.level);
  if (n.exit) lines.push('Exit: ' + n.exit);
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

const elkLayoutOptions = {
  name: 'elk',
  elk: {
    algorithm: 'layered',
    'elk.direction': 'DOWN',
    'elk.layered.spacing.nodeNodeBetweenLayers': '80',
    'elk.layered.spacing.nodeNode': '40',
    'elk.layered.nodePlacement.strategy': 'BRANDES_KOEPF',
    'elk.layered.wrapping.strategy': 'MULTI_EDGE',
    'elk.layered.compaction.connectedComponents': 'true',
    'elk.layered.compaction.postCompaction.strategy': 'EDGE_LENGTH'
  }
};

let currentLayout = null;

function stopCurrentLayout() {
  if (currentLayout) {
    currentLayout.stop();
    currentLayout = null;
  }
}

function runFullLayout(fit) {
  stopCurrentLayout();
  currentLayout = cy.layout(Object.assign({}, elkLayoutOptions, {
    animate: false,
    fit: fit,
    padding: 30,
    stop: function () { currentLayout = null; }
  }));
  currentLayout.run();
}

function positionNewNodesNearNeighbors(addedNodeIds, edges) {
  const addedSet = {};
  addedNodeIds.forEach(function (id) { addedSet[id] = true; });

  // Build adjacency from edge data, tracking direction
  const parents = {};  // parents[id] = list of source nodes that have edges TO id
  const children = {}; // children[id] = list of target nodes that id has edges TO
  edges.forEach(function (e) {
    if (!parents[e.target]) parents[e.target] = [];
    parents[e.target].push(e.source);
    if (!children[e.source]) children[e.source] = [];
    children[e.source].push(e.target);
  });

  const bb = cy.nodes().boundingBox();
  const fallbackX = (bb.x1 + bb.x2) / 2;
  const fallbackY = bb.y2 + 80;

  addedNodeIds.forEach(function (id, index) {
    // Find existing neighbors (parents go above, children go below in DOWN layout)
    const parentPositions = [];
    (parents[id] || []).forEach(function (pid) {
      if (!addedSet[pid]) {
        const n = cy.getElementById(pid);
        if (n.nonempty()) parentPositions.push(n.position());
      }
    });

    const childPositions = [];
    (children[id] || []).forEach(function (cid) {
      if (!addedSet[cid]) {
        const n = cy.getElementById(cid);
        if (n.nonempty()) childPositions.push(n.position());
      }
    });

    const allPositions = parentPositions.concat(childPositions);
    let pos;

    if (allPositions.length > 0) {
      let ax = 0, ay = 0;
      allPositions.forEach(function (p) { ax += p.x; ay += p.y; });
      ax /= allPositions.length;
      ay /= allPositions.length;

      // Place below parents or above children, with horizontal spread to avoid overlap
      const yOffset = parentPositions.length > 0 ? 80 : (childPositions.length > 0 ? -80 : 0);
      const spread = (index - (addedNodeIds.length - 1) / 2) * 50;
      pos = { x: ax + spread, y: ay + yOffset };
    } else {
      // No connected existing nodes: place below the current graph
      pos = { x: fallbackX + (index - (addedNodeIds.length - 1) / 2) * 60, y: fallbackY + index * 40 };
    }

    cy.getElementById(id).position(pos);
  });
}

function buildNodeData(n) {
  const lvl = n.level;
  const numericLevel = (lvl !== 'Unknown' && lvl !== '' && lvl != null) ? parseInt(lvl, 10) : NaN;
  const color = isNaN(numericLevel) ? unknownColor : levelColors[numericLevel % levelColors.length];
  const borderColor = shadeColor(color, -30);
  let label = n.id;
  if (n.sn > 0 || lvl !== 'Unknown') {
    label += '\nSN:' + n.sn + '  L:' + lvl;
  }
  return { id: n.id, label: label, color: color, borderColor: borderColor, sn: n.sn, level: lvl, exit: n.exit };
}

function renderGraph(jsonStr) {
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
      cy.add({ group: 'edges', data: { id: edgeId, source: e.source, target: e.target } });
    }
  });

  // Determine layout strategy
  if (!hadElements || addedNodeIds.length === data.nodes.length) {
    // First render or entirely new graph: full layout with fit
    runFullLayout(true);
  } else if (addedNodeIds.length > 0) {
    // Position new nodes near their connected existing neighbors
    positionNewNodesNearNeighbors(addedNodeIds, data.edges);
  }
  // else: only removals or data updates — positions and viewport are already preserved

  updateLegend();
}

function updateLegend() {
  const levels = {};
  const container = document.getElementById('legend-items');
  cy.nodes().forEach(function (n) {
    let lvl = n.data('level');
    if (lvl == null) lvl = 'Unknown';
    if (levels[lvl] === undefined) levels[lvl] = true;
  });
  const keys = Object.keys(levels);
  if (keys.length === 0) {
    container.innerHTML = '<span class="legend-empty">No data</span>';
    return;
  }
  const numeric = [];
  let hasUnknown = false;
  keys.forEach(function (k) {
    if (k === 'Unknown') { hasUnknown = true; }
    else { const v = parseInt(k, 10); if (!isNaN(v)) numeric.push(v); }
  });
  numeric.sort(function (a, b) { return a - b; });
  let html = '';
  numeric.forEach(function (lvl) {
    const color = levelColors[lvl % levelColors.length];
    html += '<div class="legend-item">' +
      '<span class="legend-swatch" style="background:' + color + '"></span>' +
      'Level ' + lvl + '</div>';
  });
  if (hasUnknown) {
    html += '<div class="legend-item">' +
      '<span class="legend-swatch" style="background:' + unknownColor + '"></span>' +
      'Unknown</div>';
  }
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

function highlightNodesByExit(exitValue) {
  cy.elements().removeClass('highlighted dimmed');
  if (!exitValue) return;
  const matchingNodes = cy.nodes().filter(function (n) { return n.data('exit') === exitValue; });
  if (matchingNodes.empty()) return;
  highlightNeighborhood(matchingNodes);
  cy.animate({ fit: { eles: matchingNodes, padding: 50 }, duration: 300 });
}

function resetView() {
  if (cy.nodes().length > 0) {
    cy.fit(cy.elements(), 30);
  }
}

function resetLayout() {
  if (cy.nodes().length > 0) {
    runFullLayout(true);
  }
}
