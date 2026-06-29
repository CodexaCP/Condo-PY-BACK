import{a as yt}from"./chunk-RI5D43BO.js";import{a as xt}from"./chunk-U2LK26OM.js";import{a as _e}from"./chunk-V3JKSUGC.js";import{a as ht}from"./chunk-FFSZGCZC.js";import{a as Ge,b as ft}from"./chunk-2BP556HF.js";import{a as mt}from"./chunk-COTX3Y6T.js";import{a as ut}from"./chunk-IKNNHRNW.js";import"./chunk-QFMBBYS2.js";import"./chunk-VP6LVJQE.js";import{a as ct}from"./chunk-CPZ7YTX6.js";import"./chunk-BZ22S6RY.js";import{a as Ze}from"./chunk-LQEBGFMS.js";import{a as fe}from"./chunk-BYNY62TV.js";import"./chunk-4YFFB75L.js";import{a as We}from"./chunk-H55WB7EP.js";import{a as gt}from"./chunk-2TXRLIKG.js";import{a as Xe,c as Ye,e as et,f as tt,g as nt,h as it,i as ot,k as at,l as rt,m as lt,p as pt,r as st}from"./chunk-FONRXXQ6.js";import{a as je,e as dt,f as _t}from"./chunk-A6ED6P4C.js";import{I as Ne,N as ue,Q as me,R as He,S as A,T as Qe,V as Je,r as pe,s as F}from"./chunk-XOEMQACY.js";import{C as se,D as ce,E as de,a as Ie,d as Z,e as Q,f as G,j as Oe,y as $e,z as qe}from"./chunk-6ILJPY3J.js";import{a as he}from"./chunk-XKFL72TH.js";import{a as ge}from"./chunk-6AJLGYFZ.js";import{$ as O,$a as d,Ab as be,Cb as K,Da as ve,Db as N,Eb as $,Gb as ne,Hb as De,I as Me,Ia as z,Ib as P,J as D,Jb as ie,Kb as Ce,La as Y,M as J,Ma as ee,Na as _,Nb as V,O as w,Qb as oe,T as u,Ta as x,Tb as we,U as m,Ub as ze,V as L,Wb as f,Xb as B,Z as Ve,_a as r,aa as ke,ab as c,bb as S,da as X,fb as E,gb as M,gc as ae,ha as j,hb as k,hc as re,ib as C,ic as q,jc as Re,kb as y,kc as le,lb as l,mb as Pe,mc as H,nb as Fe,ob as te,pb as Le,qb as v,rb as b,sa as Ae,sb as W,tb as Be,tc as Ue,u as Ee,ua as p,vb as R,vc as Ke,wb as h,xb as g,yb as I,zb as U}from"./chunk-I63UPHI4.js";var vt=`
    .p-chip {
        display: inline-flex;
        align-items: center;
        background: dt('chip.background');
        color: dt('chip.color');
        border-radius: dt('chip.border.radius');
        padding-block: dt('chip.padding.y');
        padding-inline: dt('chip.padding.x');
        gap: dt('chip.gap');
    }

    .p-chip-icon {
        color: dt('chip.icon.color');
        font-size: dt('chip.icon.font.size');
        width: dt('chip.icon.size');
        height: dt('chip.icon.size');
    }

    .p-chip-image {
        border-radius: 50%;
        width: dt('chip.image.width');
        height: dt('chip.image.height');
        margin-inline-start: calc(-1 * dt('chip.padding.y'));
    }

    .p-chip:has(.p-chip-remove-icon) {
        padding-inline-end: dt('chip.padding.y');
    }

    .p-chip:has(.p-chip-image) {
        padding-block-start: calc(dt('chip.padding.y') / 2);
        padding-block-end: calc(dt('chip.padding.y') / 2);
    }

    .p-chip-remove-icon {
        cursor: pointer;
        font-size: dt('chip.remove.icon.size');
        width: dt('chip.remove.icon.size');
        height: dt('chip.remove.icon.size');
        color: dt('chip.remove.icon.color');
        border-radius: 50%;
        transition:
            outline-color dt('chip.transition.duration'),
            box-shadow dt('chip.transition.duration');
        outline-color: transparent;
    }

    .p-chip-remove-icon:focus-visible {
        box-shadow: dt('chip.remove.icon.focus.ring.shadow');
        outline: dt('chip.remove.icon.focus.ring.width') dt('chip.remove.icon.focus.ring.style') dt('chip.remove.icon.focus.ring.color');
        outline-offset: dt('chip.remove.icon.focus.ring.offset');
    }
`;var Ft=["removeicon"],Lt=["*"];function Bt(t,a){if(t&1){let e=C();d(0,"img",4),y("error",function(i){u(e);let o=l();return m(o.imageError(i))}),c()}if(t&2){let e=l();h(e.cx("image")),r("pBind",e.ptm("image"))("src",e.image,Ae)("alt",e.alt)}}function Dt(t,a){if(t&1&&S(0,"span",6),t&2){let e=l(2);h(e.icon),r("pBind",e.ptm("icon"))("ngClass",e.cx("icon"))}}function zt(t,a){if(t&1&&_(0,Dt,1,4,"span",5),t&2){let e=l();r("ngIf",e.icon)}}function Rt(t,a){if(t&1&&(d(0,"div",7),g(1),c()),t&2){let e=l();h(e.cx("label")),r("pBind",e.ptm("label")),p(),I(e.label)}}function Ut(t,a){if(t&1){let e=C();d(0,"span",11),y("click",function(i){u(e);let o=l(3);return m(o.close(i))})("keydown",function(i){u(e);let o=l(3);return m(o.onKeydown(i))}),c()}if(t&2){let e=l(3);h(e.removeIcon),r("pBind",e.ptm("removeIcon"))("ngClass",e.cx("removeIcon")),x("tabindex",e.disabled?-1:0)("aria-label",e.removeAriaLabel)}}function Kt(t,a){if(t&1){let e=C();L(),d(0,"svg",12),y("click",function(i){u(e);let o=l(3);return m(o.close(i))})("keydown",function(i){u(e);let o=l(3);return m(o.onKeydown(i))}),c()}if(t&2){let e=l(3);h(e.cx("removeIcon")),r("pBind",e.ptm("removeIcon")),x("tabindex",e.disabled?-1:0)("aria-label",e.removeAriaLabel)}}function Nt(t,a){if(t&1&&(E(0),_(1,Ut,1,6,"span",9)(2,Kt,1,5,"svg",10),M()),t&2){let e=l(2);p(),r("ngIf",e.removeIcon),p(),r("ngIf",!e.removeIcon)}}function $t(t,a){}function qt(t,a){t&1&&_(0,$t,0,0,"ng-template")}function Ht(t,a){if(t&1){let e=C();d(0,"span",13),y("click",function(i){u(e);let o=l(2);return m(o.close(i))})("keydown",function(i){u(e);let o=l(2);return m(o.onKeydown(i))}),_(1,qt,1,0,null,14),c()}if(t&2){let e=l(2);h(e.cx("removeIcon")),r("pBind",e.ptm("removeIcon")),x("tabindex",e.disabled?-1:0)("aria-label",e.removeAriaLabel),p(),r("ngTemplateOutlet",e.removeIconTemplate||e._removeIconTemplate)}}function Qt(t,a){if(t&1&&(E(0),_(1,Nt,3,2,"ng-container",3)(2,Ht,2,6,"span",8),M()),t&2){let e=l();p(),r("ngIf",!e.removeIconTemplate&&!e._removeIconTemplate),p(),r("ngIf",e.removeIconTemplate||e._removeIconTemplate)}}var Gt={root:({instance:t})=>({display:!t.visible&&"none"})},jt={root:({instance:t})=>["p-chip p-component",{"p-disabled":t.disabled}],image:"p-chip-image",icon:"p-chip-icon",label:"p-chip-label",removeIcon:"p-chip-remove-icon"},bt=(()=>{class t extends ue{name="chip";style=vt;classes=jt;inlineStyles=Gt;static \u0275fac=(()=>{let e;return function(i){return(e||(e=j(t)))(i||t)}})();static \u0275prov=D({token:t,factory:t.\u0275fac})}return t})();var Ct=new J("CHIP_INSTANCE"),It=(()=>{class t extends He{$pcChip=w(Ct,{optional:!0,skipSelf:!0})??void 0;bindDirectiveInstance=w(A,{self:!0});onAfterViewChecked(){this.bindDirectiveInstance.setAttrs(this.ptms(["host","root"]))}label;icon;image;alt;styleClass;disabled=!1;removable=!1;removeIcon;onRemove=new O;onImageError=new O;visible=!0;get removeAriaLabel(){return this.config.getTranslation(de.ARIA).removeLabel}get chipProps(){return this._chipProps}set chipProps(e){this._chipProps=e,e&&typeof e=="object"&&Object.entries(e).forEach(([n,i])=>this[`_${n}`]!==i&&(this[`_${n}`]=i))}_chipProps;_componentStyle=w(bt);removeIconTemplate;templates;_removeIconTemplate;onAfterContentInit(){this.templates.forEach(e=>{switch(e.getType()){case"removeicon":this._removeIconTemplate=e.template;break;default:this._removeIconTemplate=e.template;break}})}onChanges(e){if(e.chipProps&&e.chipProps.currentValue){let{currentValue:n}=e.chipProps;n.label!==void 0&&(this.label=n.label),n.icon!==void 0&&(this.icon=n.icon),n.image!==void 0&&(this.image=n.image),n.alt!==void 0&&(this.alt=n.alt),n.styleClass!==void 0&&(this.styleClass=n.styleClass),n.removable!==void 0&&(this.removable=n.removable),n.removeIcon!==void 0&&(this.removeIcon=n.removeIcon)}}close(e){this.visible=!1,this.onRemove.emit(e)}onKeydown(e){(e.key==="Enter"||e.key==="Backspace")&&this.close(e)}imageError(e){this.onImageError.emit(e)}get dataP(){return this.cn({removable:this.removable})}static \u0275fac=(()=>{let e;return function(i){return(e||(e=j(t)))(i||t)}})();static \u0275cmp=z({type:t,selectors:[["p-chip"]],contentQueries:function(n,i,o){if(n&1&&te(o,Ft,4)(o,se,4),n&2){let s;v(s=b())&&(i.removeIconTemplate=s.first),v(s=b())&&(i.templates=s)}},hostVars:6,hostBindings:function(n,i){n&2&&(x("aria-label",i.label)("data-p",i.dataP),R(i.sx("root")),h(i.cn(i.cx("root"),i.styleClass)))},inputs:{label:"label",icon:"icon",image:"image",alt:"alt",styleClass:"styleClass",disabled:[2,"disabled","disabled",f],removable:[2,"removable","removable",f],removeIcon:"removeIcon",chipProps:"chipProps"},outputs:{onRemove:"onRemove",onImageError:"onImageError"},features:[ne([bt,{provide:Ct,useExisting:t},{provide:me,useExisting:t}]),Y([A]),ee],ngContentSelectors:Lt,decls:6,vars:4,consts:[["iconTemplate",""],[3,"pBind","class","src","alt","error",4,"ngIf","ngIfElse"],[3,"pBind","class",4,"ngIf"],[4,"ngIf"],[3,"error","pBind","src","alt"],[3,"pBind","class","ngClass",4,"ngIf"],[3,"pBind","ngClass"],[3,"pBind"],["role","button",3,"pBind","class","click","keydown",4,"ngIf"],["role","button",3,"pBind","class","ngClass","click","keydown",4,"ngIf"],["data-p-icon","times-circle","role","button",3,"pBind","class","click","keydown",4,"ngIf"],["role","button",3,"click","keydown","pBind","ngClass"],["data-p-icon","times-circle","role","button",3,"click","keydown","pBind"],["role","button",3,"click","keydown","pBind"],[4,"ngTemplateOutlet"]],template:function(n,i){if(n&1&&(Pe(),Fe(0),_(1,Bt,1,5,"img",1)(2,zt,1,1,"ng-template",null,0,V)(4,Rt,2,4,"div",2)(5,Qt,3,2,"ng-container",3)),n&2){let o=W(3);p(),r("ngIf",i.image)("ngIfElse",o),p(3),r("ngIf",i.label),p(),r("ngIf",i.removable)}},dependencies:[H,ae,q,le,_e,ce,A],encapsulation:2,changeDetection:0})}return t})();var Ot=`
    .p-autocomplete {
        display: inline-flex;
    }

    .p-autocomplete-loader {
        position: absolute;
        top: 50%;
        margin-top: -0.5rem;
        inset-inline-end: dt('autocomplete.padding.x');
    }

    .p-autocomplete:has(.p-autocomplete-dropdown) .p-autocomplete-loader {
        inset-inline-end: calc(dt('autocomplete.dropdown.width') + dt('autocomplete.padding.x'));
    }

    .p-autocomplete:has(.p-autocomplete-dropdown) .p-autocomplete-input {
        flex: 1 1 auto;
        width: 1%;
    }

    .p-autocomplete:has(.p-autocomplete-dropdown) .p-autocomplete-input,
    .p-autocomplete:has(.p-autocomplete-dropdown) .p-autocomplete-input-multiple {
        border-start-end-radius: 0;
        border-end-end-radius: 0;
    }

    .p-autocomplete-dropdown {
        cursor: pointer;
        display: inline-flex;
        user-select: none;
        align-items: center;
        justify-content: center;
        overflow: hidden;
        position: relative;
        width: dt('autocomplete.dropdown.width');
        border-start-end-radius: dt('autocomplete.dropdown.border.radius');
        border-end-end-radius: dt('autocomplete.dropdown.border.radius');
        background: dt('autocomplete.dropdown.background');
        border: 1px solid dt('autocomplete.dropdown.border.color');
        border-inline-start: 0 none;
        color: dt('autocomplete.dropdown.color');
        transition:
            background dt('autocomplete.transition.duration'),
            color dt('autocomplete.transition.duration'),
            border-color dt('autocomplete.transition.duration'),
            outline-color dt('autocomplete.transition.duration'),
            box-shadow dt('autocomplete.transition.duration');
        outline-color: transparent;
    }

    .p-autocomplete-dropdown:not(:disabled):hover {
        background: dt('autocomplete.dropdown.hover.background');
        border-color: dt('autocomplete.dropdown.hover.border.color');
        color: dt('autocomplete.dropdown.hover.color');
    }

    .p-autocomplete-dropdown:not(:disabled):active {
        background: dt('autocomplete.dropdown.active.background');
        border-color: dt('autocomplete.dropdown.active.border.color');
        color: dt('autocomplete.dropdown.active.color');
    }

    .p-autocomplete-dropdown:focus-visible {
        box-shadow: dt('autocomplete.dropdown.focus.ring.shadow');
        outline: dt('autocomplete.dropdown.focus.ring.width') dt('autocomplete.dropdown.focus.ring.style') dt('autocomplete.dropdown.focus.ring.color');
        outline-offset: dt('autocomplete.dropdown.focus.ring.offset');
    }

    .p-autocomplete-overlay {
        position: absolute;
        top: 0;
        left: 0;
        background: dt('autocomplete.overlay.background');
        color: dt('autocomplete.overlay.color');
        border: 1px solid dt('autocomplete.overlay.border.color');
        border-radius: dt('autocomplete.overlay.border.radius');
        box-shadow: dt('autocomplete.overlay.shadow');
        min-width: 100%;
    }

    .p-autocomplete-list-container {
        overflow: auto;
    }

    .p-autocomplete-list {
        margin: 0;
        list-style-type: none;
        display: flex;
        flex-direction: column;
        gap: dt('autocomplete.list.gap');
        padding: dt('autocomplete.list.padding');
    }

    .p-autocomplete-option {
        cursor: pointer;
        white-space: nowrap;
        position: relative;
        overflow: hidden;
        display: flex;
        align-items: center;
        padding: dt('autocomplete.option.padding');
        border: 0 none;
        color: dt('autocomplete.option.color');
        background: transparent;
        transition:
            background dt('autocomplete.transition.duration'),
            color dt('autocomplete.transition.duration'),
            border-color dt('autocomplete.transition.duration');
        border-radius: dt('autocomplete.option.border.radius');
    }

    .p-autocomplete-option:not(.p-autocomplete-option-selected):not(.p-disabled).p-focus {
        background: dt('autocomplete.option.focus.background');
        color: dt('autocomplete.option.focus.color');
    }

    .p-autocomplete-option:not(.p-autocomplete-option-selected):not(.p-disabled):hover {
        background: dt('autocomplete.option.focus.background');
        color: dt('autocomplete.option.focus.color');
    }

    .p-autocomplete-option-selected {
        background: dt('autocomplete.option.selected.background');
        color: dt('autocomplete.option.selected.color');
    }

    .p-autocomplete-option-selected.p-focus {
        background: dt('autocomplete.option.selected.focus.background');
        color: dt('autocomplete.option.selected.focus.color');
    }

    .p-autocomplete-option-group {
        margin: 0;
        padding: dt('autocomplete.option.group.padding');
        color: dt('autocomplete.option.group.color');
        background: dt('autocomplete.option.group.background');
        font-weight: dt('autocomplete.option.group.font.weight');
    }

    .p-autocomplete-input-multiple {
        margin: 0;
        list-style-type: none;
        cursor: text;
        overflow: hidden;
        display: flex;
        align-items: center;
        flex-wrap: wrap;
        padding: calc(dt('autocomplete.padding.y') / 2) dt('autocomplete.padding.x');
        gap: calc(dt('autocomplete.padding.y') / 2);
        color: dt('autocomplete.color');
        background: dt('autocomplete.background');
        border: 1px solid dt('autocomplete.border.color');
        border-radius: dt('autocomplete.border.radius');
        width: 100%;
        transition:
            background dt('autocomplete.transition.duration'),
            color dt('autocomplete.transition.duration'),
            border-color dt('autocomplete.transition.duration'),
            outline-color dt('autocomplete.transition.duration'),
            box-shadow dt('autocomplete.transition.duration');
        outline-color: transparent;
        box-shadow: dt('autocomplete.shadow');
    }

    .p-autocomplete-input-multiple.p-disabled {
        opacity: 1;
        background: dt('autocomplete.disabled.background');
        color: dt('autocomplete.disabled.color');
    }

    .p-autocomplete-input-multiple:not(.p-disabled):hover {
        border-color: dt('autocomplete.hover.border.color');
    }

    .p-autocomplete.p-focus .p-autocomplete-input-multiple:not(.p-disabled) {
        border-color: dt('autocomplete.focus.border.color');
        box-shadow: dt('autocomplete.focus.ring.shadow');
        outline: dt('autocomplete.focus.ring.width') dt('autocomplete.focus.ring.style') dt('autocomplete.focus.ring.color');
        outline-offset: dt('autocomplete.focus.ring.offset');
    }

    .p-autocomplete.p-invalid .p-autocomplete-input-multiple {
        border-color: dt('autocomplete.invalid.border.color');
    }

    .p-variant-filled.p-autocomplete-input-multiple {
        background: dt('autocomplete.filled.background');
    }

    .p-autocomplete-input-multiple.p-variant-filled:not(.p-disabled):hover {
        background: dt('autocomplete.filled.hover.background');
    }

    .p-autocomplete.p-focus .p-autocomplete-input-multiple.p-variant-filled:not(.p-disabled) {
        background: dt('autocomplete.filled.focus.background');
    }

    .p-autocomplete-chip.p-chip {
        padding-block-start: calc(dt('autocomplete.padding.y') / 2);
        padding-block-end: calc(dt('autocomplete.padding.y') / 2);
        border-radius: dt('autocomplete.chip.border.radius');
    }

    .p-autocomplete-input-multiple:has(.p-autocomplete-chip) {
        padding-inline-start: calc(dt('autocomplete.padding.y') / 2);
        padding-inline-end: calc(dt('autocomplete.padding.y') / 2);
    }

    .p-autocomplete-chip-item.p-focus .p-autocomplete-chip {
        background: dt('autocomplete.chip.focus.background');
        color: dt('autocomplete.chip.focus.color');
    }

    .p-autocomplete-input-chip {
        flex: 1 1 auto;
        display: inline-flex;
        padding-block-start: calc(dt('autocomplete.padding.y') / 2);
        padding-block-end: calc(dt('autocomplete.padding.y') / 2);
    }

    .p-autocomplete-input-chip input {
        border: 0 none;
        outline: 0 none;
        background: transparent;
        margin: 0;
        padding: 0;
        box-shadow: none;
        border-radius: 0;
        width: 100%;
        font-family: inherit;
        font-feature-settings: inherit;
        font-size: 1rem;
        color: inherit;
    }

    .p-autocomplete-input-chip input::placeholder {
        color: dt('autocomplete.placeholder.color');
    }

    .p-autocomplete.p-invalid .p-autocomplete-input-chip input::placeholder {
        color: dt('autocomplete.invalid.placeholder.color');
    }

    .p-autocomplete-empty-message {
        padding: dt('autocomplete.empty.message.padding');
    }

    .p-autocomplete-fluid {
        display: flex;
    }

    .p-autocomplete-fluid:has(.p-autocomplete-dropdown) .p-autocomplete-input {
        width: 1%;
    }

    .p-autocomplete:has(.p-inputtext-sm) .p-autocomplete-dropdown {
        width: dt('autocomplete.dropdown.sm.width');
    }

    .p-autocomplete:has(.p-inputtext-sm) .p-autocomplete-dropdown .p-icon {
        font-size: dt('form.field.sm.font.size');
        width: dt('form.field.sm.font.size');
        height: dt('form.field.sm.font.size');
    }

    .p-autocomplete:has(.p-inputtext-lg) .p-autocomplete-dropdown {
        width: dt('autocomplete.dropdown.lg.width');
    }

    .p-autocomplete:has(.p-inputtext-lg) .p-autocomplete-dropdown .p-icon {
        font-size: dt('form.field.lg.font.size');
        width: dt('form.field.lg.font.size');
        height: dt('form.field.lg.font.size');
    }

    .p-autocomplete-clear-icon {
        position: absolute;
        top: 50%;
        margin-top: -0.5rem;
        cursor: pointer;
        color: dt('form.field.icon.color');
        inset-inline-end: dt('autocomplete.padding.x');
    }

    .p-autocomplete:has(.p-autocomplete-dropdown) .p-autocomplete-clear-icon {
        inset-inline-end: calc(dt('autocomplete.padding.x') + dt('autocomplete.dropdown.width'));
    }

    .p-autocomplete:has(.p-autocomplete-clear-icon) .p-autocomplete-input {
        padding-inline-end: calc((dt('form.field.padding.x') * 2) + dt('icon.size'));
    }

    .p-inputgroup .p-autocomplete-dropdown {
        border-radius: 0;
    }

    .p-inputgroup > .p-autocomplete:last-child:has(.p-autocomplete-dropdown) > .p-autocomplete-input {
        border-start-end-radius: 0;
        border-end-end-radius: 0;
    }

    .p-inputgroup > .p-autocomplete:last-child .p-autocomplete-dropdown {
        border-start-end-radius: dt('autocomplete.dropdown.border.radius');
        border-end-end-radius: dt('autocomplete.dropdown.border.radius');
    }
`;var Wt=["item"],Zt=["empty"],Jt=["header"],Xt=["footer"],Yt=["selecteditem"],en=["group"],tn=["loader"],nn=["removeicon"],on=["loadingicon"],an=["clearicon"],rn=["dropdownicon"],ln=["focusInput"],pn=["multiIn"],sn=["multiContainer"],cn=["ddBtn"],dn=["items"],un=["scroller"],mn=["overlay"],_n=t=>({i:t}),Et=t=>({$implicit:t}),gn=(t,a,e)=>({removeCallback:t,index:a,class:e}),ye=t=>({height:t}),Mt=(t,a)=>({$implicit:t,options:a}),hn=t=>({options:t}),fn=()=>({}),yn=(t,a,e)=>({option:t,i:a,scrollerOptions:e}),xn=(t,a)=>({$implicit:t,index:a});function vn(t,a){if(t&1){let e=C();d(0,"input",18,2),y("input",function(i){u(e);let o=l();return m(o.onInput(i))})("keydown",function(i){u(e);let o=l();return m(o.onKeyDown(i))})("change",function(i){u(e);let o=l();return m(o.onInputChange(i))})("focus",function(i){u(e);let o=l();return m(o.onInputFocus(i))})("blur",function(i){u(e);let o=l();return m(o.onInputBlur(i))})("paste",function(i){u(e);let o=l();return m(o.onInputPaste(i))})("keyup",function(i){u(e);let o=l();return m(o.onInputKeyUp(i))}),c()}if(t&2){let e=l();h(e.cn(e.cx("pcInputText"),e.inputStyleClass)),r("pAutoFocus",e.autofocus)("pt",e.ptm("pcInputText"))("ngStyle",e.inputStyle)("variant",e.$variant())("invalid",e.invalid())("pSize",e.size())("fluid",e.hasFluid)("unstyled",e.unstyled()),x("type",e.type)("value",e.inputValue())("id",e.inputId)("autocomplete",e.autocomplete)("placeholder",e.placeholder)("name",e.name())("minlength",e.minlength())("min",e.min())("max",e.max())("pattern",e.pattern())("size",e.inputSize())("maxlength",e.maxlength())("tabindex",e.$disabled()?-1:e.tabindex)("required",e.required()?"":void 0)("readonly",e.readonly?"":void 0)("disabled",e.$disabled()?"":void 0)("aria-label",e.ariaLabel)("aria-labelledby",e.ariaLabelledBy)("aria-required",e.required())("aria-expanded",e.overlayVisible??!1)("aria-controls",e.overlayVisible?e.id+"_list":null)("aria-activedescendant",e.focused?e.focusedOptionId:void 0)}}function bn(t,a){if(t&1){let e=C();L(),d(0,"svg",21),y("click",function(){u(e);let i=l(2);return m(i.clear())}),c()}if(t&2){let e=l(2);h(e.cx("clearIcon")),r("pBind",e.ptm("clearIcon")),x("aria-hidden",!0)}}function Cn(t,a){}function wn(t,a){t&1&&_(0,Cn,0,0,"ng-template")}function In(t,a){if(t&1){let e=C();d(0,"span",22),y("click",function(){u(e);let i=l(2);return m(i.clear())}),_(1,wn,1,0,null,23),c()}if(t&2){let e=l(2);h(e.cx("clearIcon")),r("pBind",e.ptm("clearIcon")),x("aria-hidden",!0),p(),r("ngTemplateOutlet",e.clearIconTemplate||e._clearIconTemplate)}}function On(t,a){if(t&1&&(E(0),_(1,bn,1,4,"svg",19)(2,In,2,5,"span",20),M()),t&2){let e=l();p(),r("ngIf",!e.clearIconTemplate&&!e._clearIconTemplate),p(),r("ngIf",e.clearIconTemplate||e._clearIconTemplate)}}function Tn(t,a){t&1&&k(0)}function Sn(t,a){if(t&1){let e=C();d(0,"span",22),y("click",function(i){u(e);let o=l(2).index,s=l(2);return m(!s.readonly&&!s.$disabled()?s.removeOption(i,o):"")}),L(),S(1,"svg",31),c()}if(t&2){let e=l(4);h(e.cx("chipIcon")),r("pBind",e.ptm("chipIcon")),p(),h(e.cx("chipIcon")),x("aria-hidden",!0)}}function En(t,a){}function Mn(t,a){t&1&&_(0,En,0,0,"ng-template")}function Vn(t,a){if(t&1&&(d(0,"span",32),_(1,Mn,1,0,null,29),c()),t&2){let e=l(2).index,n=l(2);r("pBind",n.ptm("chipIcon")),x("aria-hidden",!0),p(),r("ngTemplateOutlet",n.removeIconTemplate||n._removeIconTemplate)("ngTemplateOutletContext",Ce(4,gn,n.removeOption.bind(n),e,n.cx("chipIcon")))}}function kn(t,a){if(t&1&&_(0,Sn,2,6,"span",20)(1,Vn,2,8,"span",30),t&2){let e=l(3);r("ngIf",!e.removeIconTemplate&&!e._removeIconTemplate),p(),r("ngIf",e.removeIconTemplate||e._removeIconTemplate)}}function An(t,a){if(t&1){let e=C();d(0,"li",26,5)(2,"p-chip",28),y("onRemove",function(i){let o=u(e).index,s=l(2);return m(s.readonly?"":s.removeOption(i,o))}),_(3,Tn,1,0,"ng-container",29)(4,kn,2,2,"ng-template",null,6,V),c()()}if(t&2){let e=a.$implicit,n=a.index,i=l(2);h(i.cx("chipItem",P(17,_n,n))),r("pBind",i.ptm("chipItem")),x("id",i.id+"_multiple_option_"+n)("aria-label",i.getOptionLabel(e))("aria-setsize",i.modelValue().length)("aria-posinset",n+1)("aria-selected",!0),p(2),h(i.cx("pcChip")),r("pt",i.ptm("pcChip"))("label",!i.selectedItemTemplate&&!i._selectedItemTemplate&&i.getOptionLabel(e))("disabled",i.$disabled())("removable",!0)("unstyled",i.unstyled()),p(),r("ngTemplateOutlet",i.selectedItemTemplate||i._selectedItemTemplate)("ngTemplateOutletContext",P(19,Et,e))}}function Pn(t,a){if(t&1){let e=C();d(0,"ul",24,3),y("focus",function(i){u(e);let o=l();return m(o.onMultipleContainerFocus(i))})("blur",function(i){u(e);let o=l();return m(o.onMultipleContainerBlur(i))})("keydown",function(i){u(e);let o=l();return m(o.onMultipleContainerKeyDown(i))}),_(2,An,6,21,"li",25),d(3,"li",26)(4,"input",27,4),y("input",function(i){u(e);let o=l();return m(o.onInput(i))})("keydown",function(i){u(e);let o=l();return m(o.onKeyDown(i))})("change",function(i){u(e);let o=l();return m(o.onInputChange(i))})("focus",function(i){u(e);let o=l();return m(o.onInputFocus(i))})("blur",function(i){u(e);let o=l();return m(o.onInputBlur(i))})("paste",function(i){u(e);let o=l();return m(o.onInputPaste(i))})("keyup",function(i){u(e);let o=l();return m(o.onInputKeyUp(i))}),c()()()}if(t&2){let e=l();h(e.cx("inputMultiple")),r("pBind",e.ptm("inputMultiple"))("tabindex",-1),x("data-p",e.inputMultipleDataP)("aria-orientation","horizontal")("aria-activedescendant",e.focused?e.focusedMultipleOptionId:void 0),p(2),r("ngForOf",e.modelValue()),p(),h(e.cx("inputChip")),r("pBind",e.ptm("inputChip")),p(),h(e.cx("pcInputText")),r("pAutoFocus",e.autofocus)("pBind",e.ptm("input"))("ngStyle",e.inputStyle),x("type",e.type)("id",e.inputId)("autocomplete",e.autocomplete)("name",e.name())("minlength",e.minlength())("maxlength",e.maxlength())("size",e.size())("min",e.min())("max",e.max())("pattern",e.pattern())("placeholder",e.$filled()?null:e.placeholder)("tabindex",e.$disabled()?-1:e.tabindex)("required",e.required()?"":void 0)("readonly",e.readonly?"":void 0)("disabled",e.$disabled()?"":void 0)("aria-label",e.ariaLabel)("aria-labelledby",e.ariaLabelledBy)("aria-required",e.required())("aria-expanded",e.overlayVisible??!1)("aria-controls",e.overlayVisible?e.id+"_list":null)("aria-activedescendant",e.focused?e.focusedOptionId:void 0)}}function Fn(t,a){if(t&1&&(L(),S(0,"svg",35)),t&2){let e=l(2);h(e.cx("loader")),r("pBind",e.ptm("loader"))("spin",!0),x("aria-hidden",!0)}}function Ln(t,a){}function Bn(t,a){t&1&&_(0,Ln,0,0,"ng-template")}function Dn(t,a){if(t&1&&(d(0,"span",32),_(1,Bn,1,0,null,23),c()),t&2){let e=l(2);h(e.cx("loader")),r("pBind",e.ptm("loader")),x("aria-hidden",!0),p(),r("ngTemplateOutlet",e.loadingIconTemplate||e._loadingIconTemplate)}}function zn(t,a){if(t&1&&(E(0),_(1,Fn,1,5,"svg",33)(2,Dn,2,5,"span",34),M()),t&2){let e=l();p(),r("ngIf",!e.loadingIconTemplate&&!e._loadingIconTemplate),p(),r("ngIf",e.loadingIconTemplate||e._loadingIconTemplate)}}function Rn(t,a){if(t&1&&S(0,"span",38),t&2){let e=l(2);r("ngClass",e.dropdownIcon),x("aria-hidden",!0)}}function Un(t,a){if(t&1&&(L(),S(0,"svg",40)),t&2){let e=l(3);r("pBind",e.ptm("dropdown"))}}function Kn(t,a){}function Nn(t,a){t&1&&_(0,Kn,0,0,"ng-template")}function $n(t,a){if(t&1&&(E(0),_(1,Un,1,1,"svg",39)(2,Nn,1,0,null,23),M()),t&2){let e=l(2);p(),r("ngIf",!e.dropdownIconTemplate&&!e._dropdownIconTemplate),p(),r("ngTemplateOutlet",e.dropdownIconTemplate||e._dropdownIconTemplate)}}function qn(t,a){if(t&1){let e=C();d(0,"button",36,7),y("click",function(i){u(e);let o=l();return m(o.handleDropdownClick(i))}),_(2,Rn,1,2,"span",37)(3,$n,3,2,"ng-container",14),c()}if(t&2){let e=l();h(e.cx("dropdown")),r("pBind",e.ptm("dropdown"))("disabled",e.$disabled()),x("aria-label",e.dropdownAriaLabel)("tabindex",e.tabindex),p(2),r("ngIf",e.dropdownIcon),p(),r("ngIf",!e.dropdownIcon)}}function Hn(t,a){t&1&&k(0)}function Qn(t,a){t&1&&k(0)}function Gn(t,a){if(t&1&&_(0,Qn,1,0,"ng-container",29),t&2){let e=a.$implicit,n=a.options;l(2);let i=W(6);r("ngTemplateOutlet",i)("ngTemplateOutletContext",ie(2,Mt,e,n))}}function jn(t,a){t&1&&k(0)}function Wn(t,a){if(t&1&&_(0,jn,1,0,"ng-container",29),t&2){let e=a.options,n=l(4);r("ngTemplateOutlet",n.loaderTemplate||n._loaderTemplate)("ngTemplateOutletContext",P(2,hn,e))}}function Zn(t,a){t&1&&(E(0),_(1,Wn,1,4,"ng-template",null,10,V),M())}function Jn(t,a){if(t&1){let e=C();d(0,"p-scroller",45,9),y("onLazyLoad",function(i){u(e);let o=l(2);return m(o.onLazyLoad.emit(i))}),_(2,Gn,1,5,"ng-template",null,1,V)(4,Zn,3,0,"ng-container",14),c()}if(t&2){let e=l(2);R(P(10,ye,e.scrollHeight)),r("tabindex",-1)("pt",e.ptm("virtualScroller"))("items",e.visibleOptions())("itemSize",e.virtualScrollItemSize)("autoSize",!0)("lazy",e.lazy)("options",e.virtualScrollOptions),p(4),r("ngIf",e.loaderTemplate||e._loaderTemplate)}}function Xn(t,a){t&1&&k(0)}function Yn(t,a){if(t&1&&(E(0),_(1,Xn,1,0,"ng-container",29),M()),t&2){l();let e=W(6),n=l();p(),r("ngTemplateOutlet",e)("ngTemplateOutletContext",ie(3,Mt,n.visibleOptions(),De(2,fn)))}}function ei(t,a){if(t&1&&(d(0,"span"),g(1),c()),t&2){let e=l(2).$implicit,n=l(3);p(),I(n.getOptionGroupLabel(e.optionGroup))}}function ti(t,a){t&1&&k(0)}function ni(t,a){if(t&1&&(E(0),d(1,"li",49),_(2,ei,2,1,"span",14)(3,ti,1,0,"ng-container",29),c(),M()),t&2){let e=l(),n=e.$implicit,i=e.index,o=l().options,s=l(2);p(),h(s.cx("optionGroup")),r("pBind",s.ptm("optionGroup"))("ngStyle",P(8,ye,o.itemSize+"px")),x("id",s.id+"_"+s.getOptionIndex(i,o)),p(),r("ngIf",!s.groupTemplate),p(),r("ngTemplateOutlet",s.groupTemplate)("ngTemplateOutletContext",P(10,Et,n.optionGroup))}}function ii(t,a){if(t&1&&(d(0,"span"),g(1),c()),t&2){let e=l(2).$implicit,n=l(3);p(),I(n.getOptionLabel(e))}}function oi(t,a){t&1&&k(0)}function ai(t,a){if(t&1){let e=C();E(0),d(1,"li",50),y("click",function(i){u(e);let o=l().$implicit,s=l(3);return m(s.onOptionSelect(i,o))})("mouseenter",function(i){u(e);let o=l().index,s=l().options,T=l(2);return m(T.onOptionMouseEnter(i,T.getOptionIndex(o,s)))}),_(2,ii,2,1,"span",14)(3,oi,1,0,"ng-container",29),c(),M()}if(t&2){let e=l(),n=e.$implicit,i=e.index,o=l().options,s=l(2);p(),h(s.cx("option",Ce(15,yn,n,i,o))),r("pBind",s.getPTOptions(n,o,i,"option"))("ngStyle",P(19,ye,o.itemSize+"px")),x("id",s.id+"_"+s.getOptionIndex(i,o))("aria-label",s.getOptionLabel(n))("aria-selected",s.isSelected(n))("data-p-selected",s.isSelected(n))("aria-disabled",s.isOptionDisabled(n))("data-p-focused",s.focusedOptionIndex()===s.getOptionIndex(i,o))("aria-setsize",s.ariaSetSize)("aria-posinset",s.getAriaPosInset(s.getOptionIndex(i,o))),p(),r("ngIf",!s.itemTemplate&&!s._itemTemplate),p(),r("ngTemplateOutlet",s.itemTemplate||s._itemTemplate)("ngTemplateOutletContext",ie(21,xn,n,o.getOptions?o.getOptions(i):i))}}function ri(t,a){if(t&1&&_(0,ni,4,12,"ng-container",14)(1,ai,4,24,"ng-container",14),t&2){let e=a.$implicit,n=l(3);r("ngIf",n.isOptionGroup(e)),p(),r("ngIf",!n.isOptionGroup(e))}}function li(t,a){if(t&1&&(E(0),g(1),M()),t&2){let e=l(4);p(),U(" ",e.searchResultMessageText," ")}}function pi(t,a){t&1&&k(0,null,12)}function si(t,a){if(t&1&&(d(0,"li",49),_(1,li,2,1,"ng-container",51)(2,pi,2,0,"ng-container",23),c()),t&2){let e=l().options,n=l(2);h(n.cx("emptyMessage")),r("pBind",n.ptm("emptyMessage"))("ngStyle",P(7,ye,e.itemSize+"px")),p(),r("ngIf",!n.emptyTemplate&&!n._emptyTemplate)("ngIfElse",n.empty),p(),r("ngTemplateOutlet",n.emptyTemplate||n._emptyTemplate)}}function ci(t,a){if(t&1&&(d(0,"ul",46,11),_(2,ri,2,2,"ng-template",47)(3,si,3,9,"li",48),c()),t&2){let e=a.$implicit,n=a.options,i=l(2);R(n.contentStyle),h(i.cn(i.cx("list"),n.contentStyleClass)),r("pBind",i.ptm("list")),x("id",i.id+"_list")("aria-label",i.listLabel),p(2),r("ngForOf",e),p(),r("ngIf",!e||e&&e.length===0&&i.showEmptyMessage)}}function di(t,a){t&1&&k(0)}function ui(t,a){if(t&1&&(d(0,"div",41),_(1,Hn,1,0,"ng-container",23),d(2,"div",42),_(3,Jn,5,12,"p-scroller",43)(4,Yn,2,6,"ng-container",14),c(),_(5,ci,4,9,"ng-template",null,8,V)(7,di,1,0,"ng-container",23),c(),d(8,"span",44),g(9),c()),t&2){let e=l();h(e.cn(e.cx("overlay"),e.panelStyleClass)),r("pBind",e.ptm("overlay"))("ngStyle",e.panelStyle),p(),r("ngTemplateOutlet",e.headerTemplate||e._headerTemplate),p(),h(e.cx("listContainer")),Be("max-height",e.virtualScroll?"auto":e.scrollHeight),r("pBind",e.ptm("listContainer"))("tabindex",-1),p(),r("ngIf",e.virtualScroll),p(),r("ngIf",!e.virtualScroll),p(3),r("ngTemplateOutlet",e.footerTemplate||e._footerTemplate),p(2),U(" ",e.selectedMessageText," ")}}var mi=`
${Ot}

/* For PrimeNG */
p-autoComplete.ng-invalid.ng-dirty .p-autocomplete-input,
p-autoComplete.ng-invalid.ng-dirty .p-autocomplete-input-multiple,
p-auto-complete.ng-invalid.ng-dirty .p-autocomplete-input,
p-auto-complete.ng-invalid.ng-dirty .p-autocomplete-input-multiple p-autocomplete.ng-invalid.ng-dirty .p-autocomplete-input,
p-autocomplete.ng-invalid.ng-dirty .p-autocomplete-input-multiple {
    border-color: dt('autocomplete.invalid.border.color');
}

p-autoComplete.ng-invalid.ng-dirty .p-autocomplete-input:enabled:focus,
p-autoComplete.ng-invalid.ng-dirty:not(.p-disabled).p-focus .p-autocomplete-input-multiple,
p-auto-complete.ng-invalid.ng-dirty .p-autocomplete-input:enabled:focus,
p-auto-complete.ng-invalid.ng-dirty:not(.p-disabled).p-focus .p-autocomplete-input-multiple,
p-autocomplete.ng-invalid.ng-dirty .p-autocomplete-input:enabled:focus,
p-autocomplete.ng-invalid.ng-dirty:not(.p-disabled).p-focus .p-autocomplete-input-multiple {
    border-color: dt('autocomplete.focus.border.color');
}

p-autoComplete.ng-invalid.ng-dirty .p-autocomplete-input-chip input::placeholder,
p-auto-complete.ng-invalid.ng-dirty .p-autocomplete-input-chip input::placeholder,
p-autocomplete.ng-invalid.ng-dirty .p-autocomplete-input-chip input::placeholder {
    color: dt('autocomplete.invalid.placeholder.color');
}

p-autoComplete.ng-invalid.ng-dirty .p-autocomplete-input::placeholder,
p-auto-complete.ng-invalid.ng-dirty .p-autocomplete-input::placeholder,
p-autocomplete.ng-invalid.ng-dirty .p-autocomplete-input::placeholder {
    color: dt('autocomplete.invalid.placeholder.color');
}
`,_i={root:{position:"relative"}},gi={root:({instance:t})=>["p-autocomplete p-component p-inputwrapper",{"p-invalid":t.invalid(),"p-focus":t.focused,"p-inputwrapper-filled":t.$filled(),"p-inputwrapper-focus":t.focused&&!t.$disabled()||t.autofocus||t.overlayVisible,"p-autocomplete-open":t.overlayVisible,"p-autocomplete-clearable":t.showClear&&!t.$disabled(),"p-autocomplete-fluid":t.hasFluid}],pcInputText:"p-autocomplete-input",inputMultiple:({instance:t})=>["p-autocomplete-input-multiple",{"p-disabled":t.$disabled(),"p-variant-filled":t.$variant()==="filled"}],chipItem:({instance:t,i:a})=>["p-autocomplete-chip-item",{"p-focus":t.focusedMultipleOptionIndex()===a}],pcChip:"p-autocomplete-chip",chipIcon:"p-autocomplete-chip-icon",inputChip:"p-autocomplete-input-chip",loader:"p-autocomplete-loader",dropdown:"p-autocomplete-dropdown",overlay:({instance:t})=>["p-autocomplete-overlay p-component-overlay p-component",{"p-input-filled":t.$variant()==="filled","p-ripple-disabled":t.config.ripple()===!1}],listContainer:"p-autocomplete-list-container",list:"p-autocomplete-list",optionGroup:"p-autocomplete-option-group",option:({instance:t,option:a,i:e,scrollerOptions:n})=>({"p-autocomplete-option":!0,"p-autocomplete-option-selected":t.isSelected(a),"p-focus":t.focusedOptionIndex()===t.getOptionIndex(e,n),"p-disabled":t.isOptionDisabled(a)}),emptyMessage:"p-autocomplete-empty-message",clearIcon:"p-autocomplete-clear-icon"},Tt=(()=>{class t extends ue{name="autocomplete";style=mi;classes=gi;inlineStyles=_i;static \u0275fac=(()=>{let e;return function(i){return(e||(e=j(t)))(i||t)}})();static \u0275prov=D({token:t,factory:t.\u0275fac})}return t})();var St=new J("AUTOCOMPLETE_INSTANCE"),hi={provide:Xe,useExisting:Me(()=>Se),multi:!0},Se=(()=>{class t extends ut{overlayService;zone;$pcAutoComplete=w(St,{optional:!0,skipSelf:!0})??void 0;bindDirectiveInstance=w(A,{self:!0});minLength=1;minQueryLength;delay=300;panelStyle;styleClass;panelStyleClass;inputStyle;inputId;inputStyleClass;placeholder;readonly;scrollHeight="200px";lazy=!1;virtualScroll;virtualScrollItemSize;virtualScrollOptions;autoHighlight;forceSelection;type="text";autoZIndex=!0;baseZIndex=0;ariaLabel;dropdownAriaLabel;ariaLabelledBy;dropdownIcon;unique=!0;group;completeOnFocus=!1;showClear=!1;dropdown;showEmptyMessage=!0;dropdownMode="blank";multiple;addOnTab=!1;tabindex;dataKey;emptyMessage;showTransitionOptions=".12s cubic-bezier(0, 0, 0.2, 1)";hideTransitionOptions=".1s linear";autofocus;autocomplete="off";optionGroupChildren="items";optionGroupLabel="label";overlayOptions;get suggestions(){return this._suggestions()}set suggestions(e){this._suggestions.set(e),this.handleSuggestionsChange()}optionLabel;optionValue;id;searchMessage;emptySelectionMessage;selectionMessage;autoOptionFocus=!1;selectOnFocus;searchLocale;optionDisabled;focusOnHover=!0;typeahead=!0;addOnBlur=!1;separator;appendTo=we(void 0);motionOptions=we(void 0);completeMethod=new O;onSelect=new O;onUnselect=new O;onAdd=new O;onFocus=new O;onBlur=new O;onDropdownClick=new O;onClear=new O;onInputKeydown=new O;onKeyUp=new O;onShow=new O;onHide=new O;onLazyLoad=new O;inputEL;multiInputEl;multiContainerEL;dropdownButton;itemsViewChild;scroller;overlayViewChild;itemsWrapper;itemTemplate;emptyTemplate;headerTemplate;footerTemplate;selectedItemTemplate;groupTemplate;loaderTemplate;removeIconTemplate;loadingIconTemplate;clearIconTemplate;dropdownIconTemplate;onHostClick(e){this.onContainerClick(e)}value;_suggestions=X(null);timeout;overlayVisible;suggestionsUpdated;highlightOption;highlightOptionChanged;focused=!1;loading;scrollHandler;listId;searchTimeout;dirty=!1;_itemTemplate;_groupTemplate;_selectedItemTemplate;_headerTemplate;_emptyTemplate;_footerTemplate;_loaderTemplate;_removeIconTemplate;_loadingIconTemplate;_clearIconTemplate;_dropdownIconTemplate;focusedMultipleOptionIndex=X(-1);focusedOptionIndex=X(-1);_componentStyle=w(Tt);$appendTo=oe(()=>this.appendTo()||this.config.overlayAppendTo());visibleOptions=oe(()=>this.group?this.flatOptions(this._suggestions()):this._suggestions()||[]);inputValue=oe(()=>{let e=this.modelValue(),n=this.optionValueSelected?(this.suggestions||[]).find(i=>G(i,e,this.equalityKey())):e;if(Z(e))if(typeof e=="object"||this.optionValueSelected){let i=this.getOptionLabel(n);return i??e}else return e;else return""});get focusedMultipleOptionId(){return this.focusedMultipleOptionIndex()!==-1?`${this.id}_multiple_option_${this.focusedMultipleOptionIndex()}`:null}get focusedOptionId(){return this.focusedOptionIndex()!==-1?`${this.id}_${this.focusedOptionIndex()}`:null}get searchResultMessageText(){return Z(this.visibleOptions())&&this.overlayVisible?this.searchMessageText.replaceAll("{0}",this.visibleOptions().length):this.emptySearchMessageText}get searchMessageText(){return this.searchMessage||this.config.translation.searchMessage||""}get emptySearchMessageText(){return this.emptyMessage||this.config.translation.emptySearchMessage||""}get selectionMessageText(){return this.selectionMessage||this.config.translation.selectionMessage||""}get emptySelectionMessageText(){return this.emptySelectionMessage||this.config.translation.emptySelectionMessage||""}get selectedMessageText(){return this.hasSelectedOption()?this.selectionMessageText.replaceAll("{0}",this.multiple?this.modelValue()?.length:"1"):this.emptySelectionMessageText}get ariaSetSize(){return this.visibleOptions().filter(e=>!this.isOptionGroup(e)).length}get listLabel(){return this.config.getTranslation(de.ARIA).listLabel}get virtualScrollerDisabled(){return!this.virtualScroll}get optionValueSelected(){return typeof this.modelValue()=="string"&&this.optionValue}chipItemClass(e){return this._componentStyle.classes.chipItem({instance:this,i:e})}constructor(e,n){super(),this.overlayService=e,this.zone=n}onInit(){this.id=this.id||Ne("pn_id_"),this.cd.detectChanges()}templates;onAfterContentInit(){this.templates.forEach(e=>{switch(e.getType()){case"item":this._itemTemplate=e.template;break;case"group":this._groupTemplate=e.template;break;case"selecteditem":this._selectedItemTemplate=e.template;break;case"selectedItem":this._selectedItemTemplate=e.template;break;case"header":this._headerTemplate=e.template;break;case"empty":this._emptyTemplate=e.template;break;case"footer":this._footerTemplate=e.template;break;case"loader":this._loaderTemplate=e.template;break;case"removetokenicon":this._removeIconTemplate=e.template;break;case"loadingicon":this._loadingIconTemplate=e.template;break;case"clearicon":this._clearIconTemplate=e.template;break;case"dropdownicon":this._dropdownIconTemplate=e.template;break;default:this._itemTemplate=e.template;break}})}onAfterViewChecked(){this.bindDirectiveInstance.setAttrs(this.ptms(["host","root"])),this.suggestionsUpdated&&this.overlayViewChild&&this.zone.runOutsideAngular(()=>{setTimeout(()=>{this.overlayViewChild&&this.overlayViewChild.alignOverlay()},1),this.suggestionsUpdated=!1})}handleSuggestionsChange(){if(this.loading){this._suggestions()?.length>0||this.showEmptyMessage||this.emptyTemplate?this.show():this.hide();let e=this.overlayVisible&&this.autoOptionFocus?this.findFirstFocusedOptionIndex():-1;this.focusedOptionIndex.set(e),this.suggestionsUpdated=!0,this.loading=!1,this.cd.markForCheck()}}flatOptions(e){return(e||[]).reduce((n,i,o)=>{n.push({optionGroup:i,group:!0,index:o});let s=this.getOptionGroupChildren(i);return s&&s.forEach(T=>n.push(T)),n},[])}isOptionGroup(e){return this.optionGroupLabel&&e.optionGroup&&e.group}findFirstOptionIndex(){return this.visibleOptions().findIndex(e=>this.isValidOption(e))}findLastOptionIndex(){return Oe(this.visibleOptions(),e=>this.isValidOption(e))}findFirstFocusedOptionIndex(){let e=this.findSelectedOptionIndex();return e<0?this.findFirstOptionIndex():e}findLastFocusedOptionIndex(){let e=this.findSelectedOptionIndex();return e<0?this.findLastOptionIndex():e}findSelectedOptionIndex(){return this.hasSelectedOption()?this.visibleOptions().findIndex(e=>this.isValidSelectedOption(e)):-1}findNextOptionIndex(e){let n=e<this.visibleOptions().length-1?this.visibleOptions().slice(e+1).findIndex(i=>this.isValidOption(i)):-1;return n>-1?n+e+1:e}findPrevOptionIndex(e){let n=e>0?Oe(this.visibleOptions().slice(0,e),i=>this.isValidOption(i)):-1;return n>-1?n:e}isValidSelectedOption(e){return this.isValidOption(e)&&this.isSelected(e)}isValidOption(e){return e&&!(this.isOptionDisabled(e)||this.isOptionGroup(e))}isOptionDisabled(e){return this.optionDisabled?Q(e,this.optionDisabled):!1}isSelected(e){return this.multiple?this.unique?this.modelValue()?.some(n=>G(n,e,this.equalityKey())):!1:G(this.modelValue(),e,this.equalityKey())}isOptionMatched(e,n){return this.isValidOption(e)&&this.getOptionLabel(e).toLocaleLowerCase(this.searchLocale)===n.toLocaleLowerCase(this.searchLocale)}isInputClicked(e){return e.target===this.inputEL?.nativeElement}isDropdownClicked(e){return this.dropdownButton?.nativeElement?e.target===this.dropdownButton.nativeElement||this.dropdownButton.nativeElement.contains(e.target):!1}equalityKey(){return this.optionValue?void 0:this.dataKey}onContainerClick(e){this.$disabled()||this.loading||this.isInputClicked(e)||this.isDropdownClicked(e)||(!this.overlayViewChild||!this.overlayViewChild.overlayViewChild?.nativeElement.contains(e.target))&&F(this.inputEL?.nativeElement)}handleDropdownClick(e){let n;this.overlayVisible?this.hide(!0):(F(this.inputEL?.nativeElement),n=this.inputEL?.nativeElement?.value,this.dropdownMode==="blank"?this.search(e,"","dropdown"):this.dropdownMode==="current"&&this.search(e,n,"dropdown")),this.onDropdownClick.emit({originalEvent:e,query:n})}onInput(e){if(this.typeahead){let n=this.minQueryLength||this.minLength;this.searchTimeout&&clearTimeout(this.searchTimeout);let i=e.target.value;this.maxlength()!==null&&(i=i.split("").slice(0,this.maxlength()).join("")),!this.multiple&&!this.forceSelection&&this.updateModel(i),i.length===0&&!this.multiple?(this.onClear.emit(),setTimeout(()=>{this.hide()},this.delay/2)):i.length>=n?(this.focusedOptionIndex.set(-1),this.searchTimeout=setTimeout(()=>{this.search(e,i,"input")},this.delay)):this.hide()}}onInputChange(e){this.updateInputWithForceSelection(e)}onInputFocus(e){if(this.$disabled())return;!this.dirty&&this.completeOnFocus&&this.search(e,e.target.value,"focus"),this.dirty=!0,this.focused=!0;let n=this.focusedOptionIndex()!==-1?this.focusedOptionIndex():this.overlayVisible&&this.autoOptionFocus?this.findFirstFocusedOptionIndex():-1;this.focusedOptionIndex.set(n),this.overlayVisible&&this.scrollInView(this.focusedOptionIndex()),this.onFocus.emit(e)}onMultipleContainerFocus(e){this.$disabled()||(this.focused=!0)}onMultipleContainerBlur(e){this.focusedMultipleOptionIndex.set(-1),this.focused=!1}onMultipleContainerKeyDown(e){if(this.$disabled()){e.preventDefault();return}switch(e.code){case"ArrowLeft":this.onArrowLeftKeyOnMultiple(e);break;case"ArrowRight":this.onArrowRightKeyOnMultiple(e);break;case"Backspace":this.onBackspaceKeyOnMultiple(e);break;default:break}}onInputBlur(e){if(this.dirty=!1,this.focused=!1,this.focusedOptionIndex.set(-1),this.addOnBlur&&this.multiple&&!this.typeahead){let n=(this.multiInputEl?.nativeElement?.value||e.target.value||"").trim();n&&!this.isSelected(n)&&(this.updateModel([...this.modelValue()||[],n]),this.onAdd.emit({originalEvent:e,value:n}),this.multiInputEl?.nativeElement?this.multiInputEl.nativeElement.value="":e.target.value="")}this.onModelTouched(),this.onBlur.emit(e)}onInputPaste(e){if(this.separator&&this.multiple&&!this.typeahead){let n=(e.clipboardData||window.clipboardData)?.getData("Text");if(n){let i=n.split(this.separator),o=[...this.modelValue()||[]];if(i.forEach(s=>{let T=s.trim();T&&!this.isSelected(T)&&o.push(T)}),o.length>(this.modelValue()||[]).length){let s=o.slice((this.modelValue()||[]).length);this.updateModel(o),s.forEach(T=>{this.onAdd.emit({originalEvent:e,value:T})}),this.multiInputEl?.nativeElement?this.multiInputEl.nativeElement.value="":e.target.value="",e.preventDefault()}}}else this.onKeyDown(e)}onInputKeyUp(e){this.onKeyUp.emit(e)}onKeyDown(e){if(this.$disabled()){e.preventDefault();return}switch(this.onInputKeydown.emit(e),e.code){case"ArrowDown":this.onArrowDownKey(e);break;case"ArrowUp":this.onArrowUpKey(e);break;case"ArrowLeft":this.onArrowLeftKey(e);break;case"ArrowRight":this.onArrowRightKey(e);break;case"Home":this.onHomeKey(e);break;case"End":this.onEndKey(e);break;case"PageDown":this.onPageDownKey(e);break;case"PageUp":this.onPageUpKey(e);break;case"Enter":case"NumpadEnter":this.onEnterKey(e);break;case"Escape":this.onEscapeKey(e);break;case"Tab":this.onTabKey(e);break;case"Backspace":this.onBackspaceKey(e);break;case"ShiftLeft":case"ShiftRight":break;default:this.handleSeparatorKey(e);break}}handleSeparatorKey(e){if(this.separator&&this.multiple&&!this.typeahead&&(this.separator===e.key||typeof this.separator=="string"&&e.key===this.separator||this.separator instanceof RegExp&&e.key.match(this.separator))){let n=(this.multiInputEl?.nativeElement?.value||e.target.value||"").trim();n&&!this.isSelected(n)&&(this.updateModel([...this.modelValue()||[],n]),this.onAdd.emit({originalEvent:e,value:n}),this.multiInputEl?.nativeElement?this.multiInputEl.nativeElement.value="":e.target.value="",e.preventDefault())}}onArrowDownKey(e){if(!this.overlayVisible)return;let n=this.focusedOptionIndex()!==-1?this.findNextOptionIndex(this.focusedOptionIndex()):this.findFirstFocusedOptionIndex();this.changeFocusedOptionIndex(e,n),e.preventDefault(),e.stopPropagation()}onArrowUpKey(e){if(this.overlayVisible)if(e.altKey)this.focusedOptionIndex()!==-1&&this.onOptionSelect(e,this.visibleOptions()[this.focusedOptionIndex()]),this.overlayVisible&&this.hide(),e.preventDefault();else{let n=this.focusedOptionIndex()!==-1?this.findPrevOptionIndex(this.focusedOptionIndex()):this.findLastFocusedOptionIndex();this.changeFocusedOptionIndex(e,n),e.preventDefault(),e.stopPropagation()}}onArrowLeftKey(e){let n=e.currentTarget;this.focusedOptionIndex.set(-1),this.multiple&&(Ie(n.value)&&this.hasSelectedOption()?(F(this.multiContainerEL?.nativeElement),this.focusedMultipleOptionIndex.set(this.modelValue().length)):e.stopPropagation())}onArrowRightKey(e){this.focusedOptionIndex.set(-1),this.multiple&&e.stopPropagation()}onHomeKey(e){let{currentTarget:n}=e,i=n.value.length;n.setSelectionRange(0,e.shiftKey?i:0),this.focusedOptionIndex.set(-1),e.preventDefault()}onEndKey(e){let{currentTarget:n}=e,i=n.value.length;n.setSelectionRange(e.shiftKey?0:i,i),this.focusedOptionIndex.set(-1),e.preventDefault()}onPageDownKey(e){this.scrollInView(this.visibleOptions().length-1),e.preventDefault()}onPageUpKey(e){this.scrollInView(0),e.preventDefault()}onEnterKey(e){if(!this.typeahead&&!this.forceSelection&&this.multiple){let n=e.target.value?.trim();n&&!this.isSelected(n)&&(this.updateModel([...this.modelValue()||[],n]),this.inputEL?.nativeElement&&(this.inputEL.nativeElement.value=""))}if(this.overlayVisible)this.focusedOptionIndex()!==-1&&this.onOptionSelect(e,this.visibleOptions()[this.focusedOptionIndex()]),this.hide();else return;e.preventDefault()}onEscapeKey(e){this.overlayVisible&&this.hide(!0),e.preventDefault()}onTabKey(e){if(this.focusedOptionIndex()!==-1){this.onOptionSelect(e,this.visibleOptions()[this.focusedOptionIndex()]);return}if(this.multiple&&!this.typeahead){let n=(this.multiInputEl?.nativeElement?.value||this.inputEL?.nativeElement?.value||"").trim();if(this.addOnTab&&n&&!this.isSelected(n)){this.updateModel([...this.modelValue()||[],n]),this.onAdd.emit({originalEvent:e,value:n}),this.multiInputEl?.nativeElement?this.multiInputEl.nativeElement.value="":this.inputEL?.nativeElement&&(this.inputEL.nativeElement.value=""),this.updateInputValue(),e.preventDefault(),this.overlayVisible&&this.hide();return}}this.overlayVisible&&this.hide()}onBackspaceKey(e){if(this.multiple){if(Z(this.modelValue())&&!this.inputEL?.nativeElement?.value){let n=this.modelValue()[this.modelValue().length-1],i=this.modelValue().slice(0,-1);this.updateModel(i),this.onUnselect.emit({originalEvent:e,value:n})}e.stopPropagation()}}onArrowLeftKeyOnMultiple(e){let n=this.focusedMultipleOptionIndex()<1?0:this.focusedMultipleOptionIndex()-1;this.focusedMultipleOptionIndex.set(n)}onArrowRightKeyOnMultiple(e){let n=this.focusedMultipleOptionIndex();n++,this.focusedMultipleOptionIndex.set(n),n>this.modelValue().length-1&&(this.focusedMultipleOptionIndex.set(-1),F(this.inputEL?.nativeElement))}onBackspaceKeyOnMultiple(e){this.focusedMultipleOptionIndex()!==-1&&this.removeOption(e,this.focusedMultipleOptionIndex())}onOptionSelect(e,n,i=!0){this.multiple?(this.inputEL?.nativeElement&&(this.inputEL.nativeElement.value=""),this.isSelected(n)||this.updateModel([...this.modelValue()||[],n])):this.updateModel(n),this.onSelect.emit({originalEvent:e,value:n}),i&&this.hide(!0)}onOptionMouseEnter(e,n){this.focusOnHover&&this.changeFocusedOptionIndex(e,n)}search(e,n,i){n!=null&&(i==="input"&&n.trim().length===0||(this.loading=!0,this.completeMethod.emit({originalEvent:e,query:n})))}removeOption(e,n){e.stopPropagation();let i=this.modelValue()[n],o=this.modelValue().filter((s,T)=>T!==n);this.updateModel(o),this.onUnselect.emit({originalEvent:e,value:i}),F(this.inputEL?.nativeElement)}updateModel(e){let n=null;e&&(n=this.multiple?e.map(i=>this.getOptionValue(i)):this.getOptionValue(e)),this.value=n,this.writeModelValue(e),this.onModelChange(n),this.updateInputValue(),this.cd.markForCheck()}updateInputValue(){this.inputEL&&this.inputEL.nativeElement&&(this.multiple?this.inputEL.nativeElement.value="":this.inputEL.nativeElement.value=this.inputValue())}updateInputWithForceSelection(e){let n=this.inputEL?.nativeElement;if(!this.forceSelection||this.overlayVisible||!n.value)return;let i=this.visibleOptions()?.find(o=>this.isOptionMatched(o,n.value));if(!i){n.value="",this.multiple||this.clear();return}i&&!this.isSelected(i)&&this.onOptionSelect(e,i)}autoUpdateModel(){if((this.selectOnFocus||this.autoHighlight)&&this.autoOptionFocus&&!this.hasSelectedOption()){let e=this.findFirstFocusedOptionIndex();this.focusedOptionIndex.set(e),this.onOptionSelect(null,this.visibleOptions()[this.focusedOptionIndex()],!1)}}scrollInView(e=-1){let n=e!==-1?`${this.id}_${e}`:this.focusedOptionId;if(this.itemsViewChild&&this.itemsViewChild.nativeElement){let i=pe(this.itemsViewChild.nativeElement,`li[id="${n}"]`);i?i.scrollIntoView&&i.scrollIntoView({block:"nearest",inline:"nearest"}):this.virtualScrollerDisabled||setTimeout(()=>{this.virtualScroll&&this.scroller?.scrollToIndex(e!==-1?e:this.focusedOptionIndex())},0)}}changeFocusedOptionIndex(e,n){this.focusedOptionIndex()!==n&&(this.focusedOptionIndex.set(n),this.scrollInView(),this.selectOnFocus&&this.onOptionSelect(e,this.visibleOptions()[n],!1))}show(e=!1){this.dirty=!0,this.overlayVisible=!0;let n=this.focusedOptionIndex()!==-1?this.focusedOptionIndex():this.autoOptionFocus?this.findFirstFocusedOptionIndex():-1;this.focusedOptionIndex.set(n),e&&F(this.inputEL?.nativeElement),e&&F(this.inputEL?.nativeElement),this.onShow.emit(),this.cd.markForCheck()}hide(e=!1){let n=()=>{this.dirty=e,this.overlayVisible=!1,this.focusedOptionIndex.set(-1),e&&F(this.inputEL?.nativeElement),this.onHide.emit(),this.updateInputWithForceSelection(null),this.cd.markForCheck()};setTimeout(()=>{n()},0)}clear(){this.updateModel(null),this.inputEL?.nativeElement&&(this.inputEL.nativeElement.value=""),this.onClear.emit()}hasSelectedOption(){return Z(this.modelValue())}getAriaPosInset(e){return(this.optionGroupLabel?e-this.visibleOptions().slice(0,e).filter(n=>this.isOptionGroup(n)).length:e)+1}getOptionLabel(e){return this.optionLabel?Q(e,this.optionLabel):e&&e.label!=null?e.label:e}getOptionValue(e){return this.optionValue?Q(e,this.optionValue):e&&e.value!=null?e.value:e}getOptionIndex(e,n){return this.virtualScrollerDisabled?e:n&&n.getItemOptions(e).index}getOptionGroupLabel(e){return this.optionGroupLabel?Q(e,this.optionGroupLabel):e&&e.label!=null?e.label:e}getOptionGroupChildren(e){return this.optionGroupChildren?Q(e,this.optionGroupChildren):e.items}getPTOptions(e,n,i,o){return this.ptm(o,{context:{option:e,index:this.getOptionIndex(i,n),selected:this.isSelected(e),focused:this.focusedOptionIndex()===this.getOptionIndex(i,n),disabled:this.isOptionDisabled(e)}})}onOverlayBeforeEnter(){if(this.itemsWrapper=pe(this.overlayViewChild.overlayViewChild?.nativeElement,this.virtualScroll?'[data-pc-name="virtualscroller"]':'[data-pc-name="pcoverlay"]'),this.virtualScroll&&(this.scroller?.setContentEl(this.itemsViewChild?.nativeElement),this.scroller?.viewInit()),this.visibleOptions()&&this.visibleOptions().length)if(this.virtualScroll){let e=this.modelValue()?this.focusedOptionIndex():-1;e!==-1&&this.scroller?.scrollToIndex(e)}else{let e=pe(this.itemsWrapper,'[data-pc-section="option"][data-p-selected="true"]');e&&e.scrollIntoView({block:"nearest",inline:"center"})}}get containerDataP(){return this.cn({fluid:this.hasFluid})}get overlayDataP(){return this.cn({[`overlay-${this.$appendTo()}`]:!0})}get inputMultipleDataP(){return this.cn({invalid:this.invalid(),disabled:this.$disabled(),focus:this.focused,fluid:this.hasFluid,filled:this.$variant()==="filled",empty:!this.$filled(),[this.size()]:this.size()})}writeControlValue(e,n){let i=this.multiple?this.visibleOptions().filter(o=>e?.some(s=>G(s,o,this.equalityKey()))):this.visibleOptions().find(o=>G(e,o,this.equalityKey()));this.value=e,n(Ie(i)?e:i),this.updateInputValue(),this.cd.markForCheck()}onDestroy(){this.scrollHandler&&(this.scrollHandler.destroy(),this.scrollHandler=null)}static \u0275fac=function(n){return new(n||t)(ve(qe),ve(ke))};static \u0275cmp=z({type:t,selectors:[["p-autoComplete"],["p-autocomplete"],["p-auto-complete"]],contentQueries:function(n,i,o){if(n&1&&te(o,Wt,5)(o,Zt,5)(o,Jt,5)(o,Xt,5)(o,Yt,5)(o,en,5)(o,tn,5)(o,nn,5)(o,on,5)(o,an,5)(o,rn,5)(o,se,4),n&2){let s;v(s=b())&&(i.itemTemplate=s.first),v(s=b())&&(i.emptyTemplate=s.first),v(s=b())&&(i.headerTemplate=s.first),v(s=b())&&(i.footerTemplate=s.first),v(s=b())&&(i.selectedItemTemplate=s.first),v(s=b())&&(i.groupTemplate=s.first),v(s=b())&&(i.loaderTemplate=s.first),v(s=b())&&(i.removeIconTemplate=s.first),v(s=b())&&(i.loadingIconTemplate=s.first),v(s=b())&&(i.clearIconTemplate=s.first),v(s=b())&&(i.dropdownIconTemplate=s.first),v(s=b())&&(i.templates=s)}},viewQuery:function(n,i){if(n&1&&Le(ln,5)(pn,5)(sn,5)(cn,5)(dn,5)(un,5)(mn,5),n&2){let o;v(o=b())&&(i.inputEL=o.first),v(o=b())&&(i.multiInputEl=o.first),v(o=b())&&(i.multiContainerEL=o.first),v(o=b())&&(i.dropdownButton=o.first),v(o=b())&&(i.itemsViewChild=o.first),v(o=b())&&(i.scroller=o.first),v(o=b())&&(i.overlayViewChild=o.first)}},hostVars:5,hostBindings:function(n,i){n&1&&y("click",function(s){return i.onHostClick(s)}),n&2&&(x("data-p",i.containerDataP),R(i.sx("root")),h(i.cn(i.cx("root"),i.styleClass)))},inputs:{minLength:[2,"minLength","minLength",B],minQueryLength:[2,"minQueryLength","minQueryLength",B],delay:[2,"delay","delay",B],panelStyle:"panelStyle",styleClass:"styleClass",panelStyleClass:"panelStyleClass",inputStyle:"inputStyle",inputId:"inputId",inputStyleClass:"inputStyleClass",placeholder:"placeholder",readonly:[2,"readonly","readonly",f],scrollHeight:"scrollHeight",lazy:[2,"lazy","lazy",f],virtualScroll:[2,"virtualScroll","virtualScroll",f],virtualScrollItemSize:[2,"virtualScrollItemSize","virtualScrollItemSize",B],virtualScrollOptions:"virtualScrollOptions",autoHighlight:[2,"autoHighlight","autoHighlight",f],forceSelection:[2,"forceSelection","forceSelection",f],type:"type",autoZIndex:[2,"autoZIndex","autoZIndex",f],baseZIndex:[2,"baseZIndex","baseZIndex",B],ariaLabel:"ariaLabel",dropdownAriaLabel:"dropdownAriaLabel",ariaLabelledBy:"ariaLabelledBy",dropdownIcon:"dropdownIcon",unique:[2,"unique","unique",f],group:[2,"group","group",f],completeOnFocus:[2,"completeOnFocus","completeOnFocus",f],showClear:[2,"showClear","showClear",f],dropdown:[2,"dropdown","dropdown",f],showEmptyMessage:[2,"showEmptyMessage","showEmptyMessage",f],dropdownMode:"dropdownMode",multiple:[2,"multiple","multiple",f],addOnTab:[2,"addOnTab","addOnTab",f],tabindex:[2,"tabindex","tabindex",B],dataKey:"dataKey",emptyMessage:"emptyMessage",showTransitionOptions:"showTransitionOptions",hideTransitionOptions:"hideTransitionOptions",autofocus:[2,"autofocus","autofocus",f],autocomplete:"autocomplete",optionGroupChildren:"optionGroupChildren",optionGroupLabel:"optionGroupLabel",overlayOptions:"overlayOptions",suggestions:"suggestions",optionLabel:"optionLabel",optionValue:"optionValue",id:"id",searchMessage:"searchMessage",emptySelectionMessage:"emptySelectionMessage",selectionMessage:"selectionMessage",autoOptionFocus:[2,"autoOptionFocus","autoOptionFocus",f],selectOnFocus:[2,"selectOnFocus","selectOnFocus",f],searchLocale:[2,"searchLocale","searchLocale",f],optionDisabled:"optionDisabled",focusOnHover:[2,"focusOnHover","focusOnHover",f],typeahead:[2,"typeahead","typeahead",f],addOnBlur:[2,"addOnBlur","addOnBlur",f],separator:"separator",appendTo:[1,"appendTo"],motionOptions:[1,"motionOptions"]},outputs:{completeMethod:"completeMethod",onSelect:"onSelect",onUnselect:"onUnselect",onAdd:"onAdd",onFocus:"onFocus",onBlur:"onBlur",onDropdownClick:"onDropdownClick",onClear:"onClear",onInputKeydown:"onInputKeydown",onKeyUp:"onKeyUp",onShow:"onShow",onHide:"onHide",onLazyLoad:"onLazyLoad"},features:[ne([hi,Tt,{provide:St,useExisting:t},{provide:me,useExisting:t}]),Y([A]),ee],decls:9,vars:14,consts:[["overlay",""],["content",""],["focusInput",""],["multiContainer",""],["focusInput","","multiIn",""],["token",""],["removeicon",""],["ddBtn",""],["buildInItems",""],["scroller",""],["loader",""],["items",""],["empty",""],["pInputText","","aria-autocomplete","list","role","combobox",3,"pAutoFocus","pt","class","ngStyle","variant","invalid","pSize","fluid","unstyled","input","keydown","change","focus","blur","paste","keyup",4,"ngIf"],[4,"ngIf"],["role","listbox",3,"pBind","class","tabindex","focus","blur","keydown",4,"ngIf"],["type","button","pRipple","",3,"pBind","class","disabled","click",4,"ngIf"],[3,"visibleChange","onBeforeEnter","onHide","hostAttrSelector","visible","options","target","appendTo","unstyled","pt","motionOptions"],["pInputText","","aria-autocomplete","list","role","combobox",3,"input","keydown","change","focus","blur","paste","keyup","pAutoFocus","pt","ngStyle","variant","invalid","pSize","fluid","unstyled"],["data-p-icon","times",3,"pBind","class","click",4,"ngIf"],[3,"pBind","class","click",4,"ngIf"],["data-p-icon","times",3,"click","pBind"],[3,"click","pBind"],[4,"ngTemplateOutlet"],["role","listbox",3,"focus","blur","keydown","pBind","tabindex"],["role","option",3,"pBind","class",4,"ngFor","ngForOf"],["role","option",3,"pBind"],["role","combobox","aria-autocomplete","list",3,"input","keydown","change","focus","blur","paste","keyup","pAutoFocus","pBind","ngStyle"],[3,"onRemove","pt","label","disabled","removable","unstyled"],[4,"ngTemplateOutlet","ngTemplateOutletContext"],[3,"pBind",4,"ngIf"],["data-p-icon","times-circle"],[3,"pBind"],["data-p-icon","spinner",3,"pBind","class","spin",4,"ngIf"],[3,"pBind","class",4,"ngIf"],["data-p-icon","spinner",3,"pBind","spin"],["type","button","pRipple","",3,"click","pBind","disabled"],[3,"ngClass",4,"ngIf"],[3,"ngClass"],["data-p-icon","chevron-down",3,"pBind",4,"ngIf"],["data-p-icon","chevron-down",3,"pBind"],[3,"pBind","ngStyle"],[3,"pBind","tabindex"],[3,"tabindex","pt","items","style","itemSize","autoSize","lazy","options","onLazyLoad",4,"ngIf"],["role","status","aria-live","polite",1,"p-hidden-accessible"],[3,"onLazyLoad","tabindex","pt","items","itemSize","autoSize","lazy","options"],["role","listbox",3,"pBind"],["ngFor","",3,"ngForOf"],["role","option",3,"pBind","class","ngStyle",4,"ngIf"],["role","option",3,"pBind","ngStyle"],["pRipple","","role","option",3,"click","mouseenter","pBind","ngStyle"],[4,"ngIf","ngIfElse"]],template:function(n,i){if(n&1){let o=C();_(0,vn,2,32,"input",13)(1,On,3,2,"ng-container",14)(2,Pn,7,37,"ul",15)(3,zn,3,2,"ng-container",14)(4,qn,4,8,"button",16),d(5,"p-overlay",17,0),$("visibleChange",function(T){return u(o),N(i.overlayVisible,T)||(i.overlayVisible=T),m(T)}),y("onBeforeEnter",function(){return u(o),m(i.onOverlayBeforeEnter())})("onHide",function(){return u(o),m(i.hide())}),_(7,ui,10,15,"ng-template",null,1,V),c()}n&2&&(r("ngIf",!i.multiple),p(),r("ngIf",i.$filled()&&!i.$disabled()&&i.showClear&&!i.loading),p(),r("ngIf",i.multiple),p(),r("ngIf",i.loading),p(),r("ngIf",i.dropdown),p(),r("hostAttrSelector",i.$attrSelector),K("visible",i.overlayVisible),r("options",i.overlayOptions)("target","@parent")("appendTo",i.$appendTo())("unstyled",i.unstyled())("pt",i.ptm("pcOverlay"))("motionOptions",i.motionOptions()),x("data-p",i.overlayDataP))},dependencies:[H,ae,re,q,le,Re,mt,ct,Je,ft,dt,_e,je,Ge,It,ce,We,Qe,A],encapsulation:2,changeDetection:0})}return t})();var xe=class t{http=w(Ke);getAll(a){let e=a?new Ue().set("buildingId",a):void 0;return this.http.get(`${ge}/unit-owners`,{params:e})}create(a){return this.http.post(`${ge}/unit-owners`,a)}delete(a){return this.http.delete(`${ge}/unit-owners/${a}`)}static \u0275fac=function(e){return new(e||t)};static \u0275prov=D({token:t,factory:t.\u0275fac,providedIn:"root"})};function fi(t,a){if(t&1&&(d(0,"div",12)(1,"span",13),g(2),c(),d(3,"span",14),g(4),c()()),t&2){let e=a.$implicit;p(2),I(e.code),p(2),I(e.buildingName)}}function yi(t,a){if(t&1&&g(0),t&2){let e=a.$implicit;be(" ",e==null?null:e.code," \u2014 ",e==null?null:e.buildingName," ")}}function xi(t,a){if(t&1){let e=C();d(0,"button",29),y("click",function(){u(e);let i=l(4);return m(i.removeOwner(i.currentPrimary))}),S(1,"span",30),c()}}function vi(t,a){if(t&1&&(d(0,"div",25),S(1,"span",26),d(2,"div")(3,"small"),g(4,"Propietario principal"),c(),d(5,"strong"),g(6),c(),d(7,"small",27),g(8),c()(),_(9,xi,2,0,"button",28),c()),t&2){let e=l(3);p(6),I(e.currentPrimary.ownerName),p(2),U("Desde ",e.currentPrimary.startDate),p(),r("ngIf",!e.isReadOnly)}}function bi(t,a){if(t&1){let e=C();d(0,"button",29),y("click",function(){u(e);let i=l(4);return m(i.removeOwner(i.currentSecondary))}),S(1,"span",30),c()}}function Ci(t,a){if(t&1&&(d(0,"div",31),S(1,"span",32),d(2,"div")(3,"small"),g(4,"Propietario 2"),c(),d(5,"strong"),g(6),c(),d(7,"small",27),g(8),c()(),_(9,bi,2,0,"button",28),c()),t&2){let e=l(3);p(6),I(e.currentSecondary.ownerName),p(2),U("Desde ",e.currentSecondary.startDate),p(),r("ngIf",!e.isReadOnly)}}function wi(t,a){if(t&1&&(d(0,"div",22),_(1,vi,10,3,"div",23)(2,Ci,10,3,"div",24),c()),t&2){let e=l(2);p(),r("ngIf",e.currentPrimary),p(),r("ngIf",e.currentSecondary)}}function Ii(t,a){t&1&&(d(0,"p",33),g(1,"Esta unidad no tiene propietarios asignados."),c())}function Oi(t,a){if(t&1&&(d(0,"option",43),g(1),c()),t&2){let e=a.$implicit;r("value",e.id),p(),I(e.fullName)}}function Ti(t,a){if(t&1){let e=C();d(0,"form",34),y("ngSubmit",function(){u(e);let i=l(2);return m(i.addOwner())}),d(1,"div",35)(2,"label",36)(3,"span"),g(4),c(),d(5,"select",37),$("ngModelChange",function(i){u(e);let o=l(2);return N(o.addForm.ownerId,i)||(o.addForm.ownerId=i),m(i)}),d(6,"option",38),g(7,"\u2014 Seleccionar propietario \u2014"),c(),_(8,Oi,2,2,"option",39),c()(),d(9,"label",36)(10,"span"),g(11,"Vigente desde"),c(),d(12,"input",40),$("ngModelChange",function(i){u(e);let o=l(2);return N(o.addForm.startDate,i)||(o.addForm.startDate=i),m(i)}),c()()(),d(13,"div",41),S(14,"p-button",42),c()()}if(t&2){let e=l(2);p(4),I(e.currentPrimary?"Propietario 2 (opcional)":"Propietario principal *"),p(),K("ngModel",e.addForm.ownerId),p(3),r("ngForOf",e.availableOwners),p(4),K("ngModel",e.addForm.startDate),p(2),r("label",e.currentPrimary?"Asignar propietario 2":"Asignar propietario principal")("loading",e.isSaving)("disabled",!e.addForm.ownerId)}}function Si(t,a){t&1&&(d(0,"p",44),S(1,"span",45),g(2," La unidad ya tiene sus 2 propietarios asignados. Quit\xE1 uno para agregar otro. "),c())}function Ei(t,a){if(t&1&&(d(0,"div",15)(1,"div",16)(2,"div",17),g(3),c(),d(4,"div")(5,"h2"),g(6),c(),d(7,"p"),g(8),c()()(),_(9,wi,3,2,"div",18)(10,Ii,2,0,"p",19)(11,Ti,15,7,"form",20)(12,Si,3,0,"p",21),c()),t&2){let e=l();p(3),I(e.selectedUnit.code),p(3),I(e.selectedUnit.buildingName),p(2),be("Piso ",e.selectedUnit.floor," \xB7 Coef. ",e.selectedUnit.coefficient.toFixed(4)),p(),r("ngIf",e.currentPrimary||e.currentSecondary),p(),r("ngIf",!e.currentPrimary&&!e.currentSecondary),p(),r("ngIf",!e.isReadOnly&&e.canAddMore),p(),r("ngIf",!e.isReadOnly&&!e.canAddMore)}}function Mi(t,a){t&1&&(d(0,"p",46),g(1,"Cargando..."),c())}function Vi(t,a){t&1&&S(0,"span")}function ki(t,a){if(t&1){let e=C();d(0,"div")(1,"p-button",53),y("onClick",function(){u(e);let i=l().$implicit,o=l(2);return m(o.removeOwner(i))}),c()()}if(t&2){let e=l(3);p(),r("rounded",!0)("text",!0)("disabled",e.isSaving)}}function Ai(t,a){if(t&1&&(d(0,"div",51)(1,"strong"),g(2),c(),d(3,"span"),g(4),c(),d(5,"span"),g(6),c(),S(7,"p-tag",52),d(8,"span"),g(9),c(),_(10,ki,2,3,"div",11),c()),t&2){let e=a.$implicit,n=l(2);p(2),I(e.unitCode),p(2),I(e.buildingName),p(2),I(e.ownerName),p(),r("value",e.isPrimary?"Principal":"Secundario")("severity",e.isPrimary?"info":"secondary"),p(2),I(e.startDate),p(),r("ngIf",!n.isReadOnly)}}function Pi(t,a){if(t&1&&(E(0),d(1,"h3",47),g(2,"Todas las asignaciones"),c(),d(3,"div",48)(4,"div",49)(5,"span"),g(6,"Unidad"),c(),d(7,"span"),g(8,"Edificio"),c(),d(9,"span"),g(10,"Propietario"),c(),d(11,"span"),g(12,"Tipo"),c(),d(13,"span"),g(14,"Desde"),c(),_(15,Vi,1,0,"span",11),c(),_(16,Ai,11,7,"div",50),c(),M()),t&2){let e=l();p(15),r("ngIf",!e.isReadOnly),p(),r("ngForOf",e.assignments)}}function Fi(t,a){t&1&&(d(0,"p",46),g(1,"No hay propietarios asignados."),c())}var Vt=class t{unitOwnersApi=w(xe);ownersApi=w(yt);unitsApi=w(xt);auth=w(Ze);destroyRef=w(Ve);cdr=w(ze);msg=w($e);get isReadOnly(){return this.auth.hasRole("CompanyAdmin")}units=[];owners=[];assignments=[];unitSuggestions=[];selectedUnit=null;loading=!0;isSaving=!1;addForm=this.emptyAddForm();get currentPrimary(){return this.assignments.find(a=>a.unitId===this.selectedUnit?.id&&a.isPrimary)??null}get currentSecondary(){return this.assignments.find(a=>a.unitId===this.selectedUnit?.id&&!a.isPrimary)??null}get canAddMore(){return!this.currentPrimary||!this.currentSecondary}get availableOwners(){if(!this.selectedUnit)return this.owners;let a=new Set(this.assignments.filter(e=>e.unitId===this.selectedUnit.id).map(e=>e.ownerId));return this.owners.filter(e=>!a.has(e.id))}ngOnInit(){Ee({assignments:this.unitOwnersApi.getAll(),units:this.unitsApi.getAll(),owners:this.ownersApi.getAll()}).pipe(he(this.destroyRef)).subscribe({next:({assignments:a,units:e,owners:n})=>{this.assignments=a,this.units=e,this.owners=n,this.loading=!1,this.cdr.markForCheck()},error:a=>{this.msg.add({severity:"error",summary:"Error",detail:fe(a,"No se pudieron cargar los datos."),life:5e3}),this.loading=!1,this.cdr.markForCheck()}})}searchUnits(a){let e=a.query.toLowerCase();this.unitSuggestions=this.units.filter(n=>n.code.toLowerCase().includes(e)||n.buildingName.toLowerCase().includes(e)||n.floor.toLowerCase().includes(e))}onUnitSelected(){this.addForm=this.emptyAddForm(),this.cdr.markForCheck()}clearUnit(){this.selectedUnit=null,this.addForm=this.emptyAddForm(),this.cdr.markForCheck()}addOwner(){if(!this.selectedUnit||!this.addForm.ownerId)return;let a=!this.currentPrimary;this.isSaving=!0,this.unitOwnersApi.create({unitId:this.selectedUnit.id,ownerId:this.addForm.ownerId,isPrimary:a,startDate:this.addForm.startDate}).pipe(he(this.destroyRef)).subscribe({next:e=>{this.assignments=[...this.assignments,e],this.addForm=this.emptyAddForm(),this.isSaving=!1,this.msg.add({severity:"success",summary:"Guardado",detail:"Propietario asignado correctamente.",life:4e3}),this.cdr.markForCheck()},error:e=>{this.msg.add({severity:"error",summary:"Error",detail:fe(e,"No se pudo asignar el propietario."),life:5e3}),this.isSaving=!1,this.cdr.markForCheck()}})}removeOwner(a){this.isSaving=!0,this.unitOwnersApi.delete(a.id).pipe(he(this.destroyRef)).subscribe({next:()=>{this.assignments=this.assignments.filter(e=>e.id!==a.id),this.isSaving=!1,this.msg.add({severity:"success",summary:"Quitado",detail:"Propietario quitado de la unidad.",life:4e3}),this.cdr.markForCheck()},error:e=>{this.msg.add({severity:"error",summary:"Error",detail:fe(e,"No se pudo quitar el propietario."),life:5e3}),this.isSaving=!1,this.cdr.markForCheck()}})}emptyAddForm(){return{ownerId:"",startDate:new Date().toISOString().slice(0,10)}}static \u0275fac=function(e){return new(e||t)};static \u0275cmp=z({type:t,selectors:[["app-assignments-page"]],decls:21,vars:8,consts:[["itemTemplate",""],["selectedItemTemplate",""],["styleClass","app-page-card"],[1,"app-toolbar"],[1,"app-page-head"],[1,"search-row"],[1,"search-wrap"],[1,"search-label"],["optionLabel","code","placeholder","Escribi el codigo o edificio...","styleClass","unit-autocomplete","appendTo","body",3,"ngModelChange","completeMethod","onSelect","onClear","ngModel","suggestions","forceSelection","dropdown"],["class","assign-panel",4,"ngIf"],["class","app-state",4,"ngIf"],[4,"ngIf"],[1,"unit-option"],[1,"unit-option-code"],[1,"unit-option-building"],[1,"assign-panel"],[1,"assign-panel-head"],[1,"assign-unit-badge"],["class","current-owners",4,"ngIf"],["class","no-owners",4,"ngIf"],["class","add-form",3,"ngSubmit",4,"ngIf"],["class","max-owners",4,"ngIf"],[1,"current-owners"],["class","owner-chip primary-chip",4,"ngIf"],["class","owner-chip secondary-chip",4,"ngIf"],[1,"owner-chip","primary-chip"],[1,"dot"],[1,"since"],["type","button","class","remove-btn","title","Quitar",3,"click",4,"ngIf"],["type","button","title","Quitar",1,"remove-btn",3,"click"],[1,"pi","pi-times"],[1,"owner-chip","secondary-chip"],[1,"dot","dot-2"],[1,"no-owners"],[1,"add-form",3,"ngSubmit"],[1,"add-form-fields"],[1,"field-block"],["name","ownerId","required","",3,"ngModelChange","ngModel"],["value",""],[3,"value",4,"ngFor","ngForOf"],["type","date","name","startDate","required","",3,"ngModelChange","ngModel"],[1,"add-form-actions"],["type","submit","icon","pi pi-user-plus",3,"label","loading","disabled"],[3,"value"],[1,"max-owners"],[1,"pi","pi-info-circle"],[1,"app-state"],[1,"section-title"],[1,"app-list"],[1,"app-row","header","assign-grid"],["class","app-row assign-grid",4,"ngFor","ngForOf"],[1,"app-row","assign-grid"],[3,"value","severity"],["type","button","icon","pi pi-trash","severity","danger","size","small",3,"onClick","rounded","text","disabled"]],template:function(e,n){if(e&1){let i=C();d(0,"p-card",2)(1,"div",3)(2,"div",4)(3,"div")(4,"h1"),g(5,"Propietarios"),c(),d(6,"p"),g(7,"Asignacion de propietarios a unidades. Cada unidad admite un propietario principal y uno opcional."),c()()()(),d(8,"div",5)(9,"div",6)(10,"label",7),g(11,"Buscar unidad"),c(),d(12,"p-autoComplete",8),$("ngModelChange",function(s){return u(i),N(n.selectedUnit,s)||(n.selectedUnit=s),m(s)}),y("completeMethod",function(s){return u(i),m(n.searchUnits(s))})("onSelect",function(){return u(i),m(n.onUnitSelected())})("onClear",function(){return u(i),m(n.clearUnit())}),_(13,fi,5,2,"ng-template",null,0,V)(15,yi,1,2,"ng-template",null,1,V),c()()(),_(17,Ei,13,8,"div",9)(18,Mi,2,0,"p",10)(19,Pi,17,2,"ng-container",11)(20,Fi,2,0,"p",10),c()}e&2&&(p(12),K("ngModel",n.selectedUnit),r("suggestions",n.unitSuggestions)("forceSelection",!0)("dropdown",!0),p(5),r("ngIf",n.selectedUnit),p(),r("ngIf",n.loading),p(),r("ngIf",!n.loading&&n.assignments.length),p(),r("ngIf",!n.loading&&!n.assignments.length))},dependencies:[H,re,q,st,ot,rt,lt,Ye,at,et,tt,pt,it,nt,Se,_t,gt,ht],styles:[".search-row[_ngcontent-%COMP%]{margin-bottom:1.5rem}.search-wrap[_ngcontent-%COMP%]{display:grid;gap:.5rem;max-width:480px}.search-label[_ngcontent-%COMP%]{font-weight:700;color:#29484f;font-size:.9rem}[_nghost-%COMP%]     .unit-autocomplete{width:100%}[_nghost-%COMP%]     .unit-autocomplete .p-autocomplete-input{width:100%;border-radius:14px;border:1.5px solid #d7e5e1;padding:.85rem 1rem;font:inherit}[_nghost-%COMP%]     .unit-autocomplete .p-autocomplete-input:focus{border-color:var(--brand-blue, #1385b6);box-shadow:0 0 0 3px #1385b61f}.unit-option[_ngcontent-%COMP%]{display:flex;align-items:center;gap:.75rem;padding:.25rem 0}.unit-option-code[_ngcontent-%COMP%]{font-weight:800;color:var(--brand-ink, #18353a);min-width:70px}.unit-option-building[_ngcontent-%COMP%]{color:var(--brand-muted, #6b878d);font-size:.88rem}.assign-panel[_ngcontent-%COMP%]{background:#ffffffe6;border:1.5px solid var(--brand-blue, #1385b6);border-radius:22px;padding:1.5rem;margin-bottom:1.5rem;box-shadow:0 6px 20px #1385b61a}.assign-panel-head[_ngcontent-%COMP%]{display:flex;align-items:center;gap:1rem;margin-bottom:1.25rem}.assign-unit-badge[_ngcontent-%COMP%]{background:var(--brand-gradient, linear-gradient(135deg, #1385b6, #0fa090));color:#fff;font-size:1.15rem;font-weight:800;border-radius:14px;padding:.6rem 1rem;white-space:nowrap}.assign-panel-head[_ngcontent-%COMP%]   h2[_ngcontent-%COMP%]{margin:0;color:var(--brand-ink, #18353a)}.assign-panel-head[_ngcontent-%COMP%]   p[_ngcontent-%COMP%]{margin:.2rem 0 0;color:var(--brand-muted, #6b878d);font-size:.85rem}.current-owners[_ngcontent-%COMP%]{display:flex;flex-wrap:wrap;gap:.75rem;margin-bottom:1.25rem}.owner-chip[_ngcontent-%COMP%]{display:flex;align-items:center;gap:.75rem;padding:.85rem 1rem;border-radius:16px;flex:1;min-width:220px}.primary-chip[_ngcontent-%COMP%]{background:#1385b612;border:1.5px solid rgba(19,133,182,.2)}.secondary-chip[_ngcontent-%COMP%]{background:#0fa0900f;border:1.5px solid rgba(15,160,144,.2)}.dot[_ngcontent-%COMP%]{width:12px;height:12px;border-radius:50%;background:var(--brand-blue, #1385b6);flex-shrink:0}.dot-2[_ngcontent-%COMP%]{background:#0fa090}.owner-chip[_ngcontent-%COMP%] > div[_ngcontent-%COMP%]{flex:1;display:grid;gap:.15rem}.owner-chip[_ngcontent-%COMP%]   small[_ngcontent-%COMP%]{color:var(--brand-muted, #6b878d);font-size:.72rem;font-weight:700;text-transform:uppercase;letter-spacing:.04em}.owner-chip[_ngcontent-%COMP%]   strong[_ngcontent-%COMP%]{color:var(--brand-ink, #18353a);font-size:.95rem}.since[_ngcontent-%COMP%]{font-weight:400!important;font-size:.78rem!important;text-transform:none!important;letter-spacing:0!important}.remove-btn[_ngcontent-%COMP%]{background:none;border:none;cursor:pointer;color:#94a3b8;padding:.35rem;border-radius:8px;transition:background .1s,color .1s;line-height:1}.remove-btn[_ngcontent-%COMP%]:hover{background:#c94d3f1a;color:#c94d3f}.no-owners[_ngcontent-%COMP%]{color:var(--brand-muted, #6b878d);margin:0 0 1.25rem;font-style:italic}.add-form[_ngcontent-%COMP%]{border-top:1px solid rgba(19,133,182,.12);padding-top:1.25rem;display:grid;gap:1rem}.add-form-fields[_ngcontent-%COMP%]{display:grid;grid-template-columns:1fr 200px;gap:1rem;align-items:end}.field-block[_ngcontent-%COMP%]{display:grid;gap:.45rem}.field-block[_ngcontent-%COMP%] > span[_ngcontent-%COMP%]{font-weight:700;color:#29484f;font-size:.88rem}.field-block[_ngcontent-%COMP%]   select[_ngcontent-%COMP%], .field-block[_ngcontent-%COMP%]   input[_ngcontent-%COMP%]{border:1.5px solid #d7e5e1;border-radius:14px;padding:.85rem 1rem;font:inherit;background:#fff;color:#18353a;width:100%;box-sizing:border-box}.field-block[_ngcontent-%COMP%]   select[_ngcontent-%COMP%]:focus, .field-block[_ngcontent-%COMP%]   input[_ngcontent-%COMP%]:focus{outline:none;border-color:var(--brand-blue, #1385b6);box-shadow:0 0 0 3px #1385b61f}.add-form-actions[_ngcontent-%COMP%]{display:flex;justify-content:flex-end}.max-owners[_ngcontent-%COMP%]{color:var(--brand-muted, #6b878d);font-size:.88rem;border-top:1px solid rgba(19,133,182,.12);padding-top:1rem;margin:0}.max-owners[_ngcontent-%COMP%]   .pi[_ngcontent-%COMP%]{margin-right:.35rem}.section-title[_ngcontent-%COMP%]{color:var(--brand-ink, #18353a);margin:0 0 .75rem;font-size:1rem}.assign-grid[_ngcontent-%COMP%]{grid-template-columns:.7fr 1fr 1fr .7fr .8fr 48px}@media(max-width:860px){.add-form-fields[_ngcontent-%COMP%], .assign-grid[_ngcontent-%COMP%]{grid-template-columns:1fr}}"]})};export{Vt as AssignmentsPageComponent};
