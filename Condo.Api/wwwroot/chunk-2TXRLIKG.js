import{N as $,Q as z,R as G,S as h,T as J}from"./chunk-XOEMQACY.js";import{A as O,B as H,C as P,D as V,f as A}from"./chunk-6ILJPY3J.js";import{$a as f,Gb as Q,Ia as F,J as x,La as S,M as B,Ma as D,Na as p,O as T,_a as i,ab as u,da as I,fb as v,gb as C,ha as b,hb as y,ic as R,kc as q,lb as _,mb as k,mc as w,nb as g,ob as N,qb as c,rb as d,ua as r,vb as j,wb as m,xb as E,yb as M}from"./chunk-I63UPHI4.js";var K=`
    .p-card {
        background: dt('card.background');
        color: dt('card.color');
        box-shadow: dt('card.shadow');
        border-radius: dt('card.border.radius');
        display: flex;
        flex-direction: column;
    }

    .p-card-caption {
        display: flex;
        flex-direction: column;
        gap: dt('card.caption.gap');
    }

    .p-card-body {
        padding: dt('card.body.padding');
        display: flex;
        flex-direction: column;
        gap: dt('card.body.gap');
    }

    .p-card-title {
        font-size: dt('card.title.font.size');
        font-weight: dt('card.title.font.weight');
    }

    .p-card-subtitle {
        color: dt('card.subtitle.color');
    }
`;var W=["header"],X=["title"],Y=["subtitle"],Z=["content"],ee=["footer"],te=["*",[["p-header"]],[["p-footer"]]],ne=["*","p-header","p-footer"];function ie(t,l){t&1&&y(0)}function ae(t,l){if(t&1&&(f(0,"div",1),g(1,1),p(2,ie,1,0,"ng-container",2),u()),t&2){let e=_();m(e.cx("header")),i("pBind",e.ptm("header")),r(2),i("ngTemplateOutlet",e.headerTemplate||e._headerTemplate)}}function re(t,l){if(t&1&&(v(0),E(1),C()),t&2){let e=_(2);r(),M(e.header)}}function oe(t,l){t&1&&y(0)}function le(t,l){if(t&1&&(f(0,"div",1),p(1,re,2,1,"ng-container",3)(2,oe,1,0,"ng-container",2),u()),t&2){let e=_();m(e.cx("title")),i("pBind",e.ptm("title")),r(),i("ngIf",e.header&&!e._titleTemplate&&!e.titleTemplate),r(),i("ngTemplateOutlet",e.titleTemplate||e._titleTemplate)}}function pe(t,l){if(t&1&&(v(0),E(1),C()),t&2){let e=_(2);r(),M(e.subheader)}}function ce(t,l){t&1&&y(0)}function de(t,l){if(t&1&&(f(0,"div",1),p(1,pe,2,1,"ng-container",3)(2,ce,1,0,"ng-container",2),u()),t&2){let e=_();m(e.cx("subtitle")),i("pBind",e.ptm("subtitle")),r(),i("ngIf",e.subheader&&!e._subtitleTemplate&&!e.subtitleTemplate),r(),i("ngTemplateOutlet",e.subtitleTemplate||e._subtitleTemplate)}}function se(t,l){t&1&&y(0)}function me(t,l){t&1&&y(0)}function fe(t,l){if(t&1&&(f(0,"div",1),g(1,2),p(2,me,1,0,"ng-container",2),u()),t&2){let e=_();m(e.cx("footer")),i("pBind",e.ptm("footer")),r(2),i("ngTemplateOutlet",e.footerTemplate||e._footerTemplate)}}var ue=`
    ${K}

    .p-card {
        display: block;
    }
`,_e={root:"p-card p-component",header:"p-card-header",body:"p-card-body",caption:"p-card-caption",title:"p-card-title",subtitle:"p-card-subtitle",content:"p-card-content",footer:"p-card-footer"},L=(()=>{class t extends ${name="card";style=ue;classes=_e;static \u0275fac=(()=>{let e;return function(n){return(e||(e=b(t)))(n||t)}})();static \u0275prov=x({token:t,factory:t.\u0275fac})}return t})();var U=new B("CARD_INSTANCE"),Ae=(()=>{class t extends G{$pcCard=T(U,{optional:!0,skipSelf:!0})??void 0;bindDirectiveInstance=T(h,{self:!0});_componentStyle=T(L);onAfterViewChecked(){this.bindDirectiveInstance.setAttrs(this.ptms(["host","root"]))}header;subheader;set style(e){A(this._style(),e)||(this._style.set(e),this.el?.nativeElement&&e&&Object.keys(e).forEach(o=>{this.el.nativeElement.style[o]=e[o]}))}get style(){return this._style()}styleClass;headerFacet;footerFacet;headerTemplate;titleTemplate;subtitleTemplate;contentTemplate;footerTemplate;_headerTemplate;_titleTemplate;_subtitleTemplate;_contentTemplate;_footerTemplate;_style=I(null);getBlockableElement(){return this.el.nativeElement.children[0]}templates;onAfterContentInit(){this.templates.forEach(e=>{switch(e.getType()){case"header":this._headerTemplate=e.template;break;case"title":this._titleTemplate=e.template;break;case"subtitle":this._subtitleTemplate=e.template;break;case"content":this._contentTemplate=e.template;break;case"footer":this._footerTemplate=e.template;break;default:this._contentTemplate=e.template;break}})}static \u0275fac=(()=>{let e;return function(n){return(e||(e=b(t)))(n||t)}})();static \u0275cmp=F({type:t,selectors:[["p-card"]],contentQueries:function(o,n,s){if(o&1&&N(s,O,5)(s,H,5)(s,W,4)(s,X,4)(s,Y,4)(s,Z,4)(s,ee,4)(s,P,4),o&2){let a;c(a=d())&&(n.headerFacet=a.first),c(a=d())&&(n.footerFacet=a.first),c(a=d())&&(n.headerTemplate=a.first),c(a=d())&&(n.titleTemplate=a.first),c(a=d())&&(n.subtitleTemplate=a.first),c(a=d())&&(n.contentTemplate=a.first),c(a=d())&&(n.footerTemplate=a.first),c(a=d())&&(n.templates=a)}},hostVars:4,hostBindings:function(o,n){o&2&&(j(n._style()),m(n.cn(n.cx("root"),n.styleClass)))},inputs:{header:"header",subheader:"subheader",style:"style",styleClass:"styleClass"},features:[Q([L,{provide:U,useExisting:t},{provide:z,useExisting:t}]),S([h]),D],ngContentSelectors:ne,decls:8,vars:11,consts:[[3,"pBind","class",4,"ngIf"],[3,"pBind"],[4,"ngTemplateOutlet"],[4,"ngIf"]],template:function(o,n){o&1&&(k(te),p(0,ae,3,4,"div",0),f(1,"div",1),p(2,le,3,5,"div",0)(3,de,3,5,"div",0),f(4,"div",1),g(5),p(6,se,1,0,"ng-container",2),u(),p(7,fe,3,4,"div",0),u()),o&2&&(i("ngIf",n.headerFacet||n.headerTemplate||n._headerTemplate),r(),m(n.cx("body")),i("pBind",n.ptm("body")),r(),i("ngIf",n.header||n.titleTemplate||n._titleTemplate),r(),i("ngIf",n.subheader||n.subtitleTemplate||n._subtitleTemplate),r(),m(n.cx("content")),i("pBind",n.ptm("content")),r(2),i("ngTemplateOutlet",n.contentTemplate||n._contentTemplate),r(),i("ngIf",n.footerFacet||n.footerTemplate||n._footerTemplate))},dependencies:[w,R,q,V,J,h],encapsulation:2,changeDetection:0})}return t})();export{Ae as a};
